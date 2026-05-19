using Amazon.Runtime;
using Amazon.S3;
using CloudinaryDotNet;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<CookiePolicyOptions>(options =>
{
    options.MinimumSameSitePolicy = SameSiteMode.Lax;
    options.Secure = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

builder.Configuration.AddEnvironmentVariables();

var connectionString = ResolveConnectionString(builder.Configuration);
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is missing. Set it with user-secrets, ConnectionStrings__DefaultConnection, or DATABASE_URL.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
        .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

var r2Settings = new CloudflareR2Settings
{
    AccountId = GetConfiguredValue(builder.Configuration, "CloudflareR2:AccountId", "CloudflareR2__AccountId", "R2_ACCOUNT_ID", "CLOUDFLARE_R2_ACCOUNT_ID") ?? string.Empty,
    AccessKeyId = GetConfiguredValue(builder.Configuration, "CloudflareR2:AccessKeyId", "CloudflareR2__AccessKeyId", "R2_ACCESS_KEY_ID", "CLOUDFLARE_R2_ACCESS_KEY_ID") ?? string.Empty,
    SecretAccessKey = GetConfiguredValue(builder.Configuration, "CloudflareR2:SecretAccessKey", "CloudflareR2__SecretAccessKey", "R2_SECRET_ACCESS_KEY", "CLOUDFLARE_R2_SECRET_ACCESS_KEY") ?? string.Empty,
    BucketName = GetConfiguredValue(builder.Configuration, "CloudflareR2:BucketName", "CloudflareR2__BucketName", "R2_BUCKET_NAME", "R2_BUCKET", "CLOUDFLARE_R2_BUCKET_NAME") ?? string.Empty,
    PublicBaseUrl = GetConfiguredValue(builder.Configuration, "CloudflareR2:PublicBaseUrl", "CloudflareR2__PublicBaseUrl", "R2_PUBLIC_BASE_URL", "CLOUDFLARE_R2_PUBLIC_BASE_URL") ?? string.Empty,
    Endpoint = GetConfiguredValue(builder.Configuration, "CloudflareR2:Endpoint", "CloudflareR2__Endpoint", "R2_ENDPOINT", "CLOUDFLARE_R2_ENDPOINT")
};

var cloudName = GetConfiguredValue(builder.Configuration, "Cloudinary:CloudName", "CLOUDINARY_CLOUD_NAME");
var apiKey = GetConfiguredValue(builder.Configuration, "Cloudinary:ApiKey", "CLOUDINARY_API_KEY");
var apiSecret = GetConfiguredValue(builder.Configuration, "Cloudinary:ApiSecret", "CLOUDINARY_API_SECRET");

if (r2Settings.IsConfigured)
{
    var endpoint = string.IsNullOrWhiteSpace(r2Settings.Endpoint)
        ? $"https://{r2Settings.AccountId}.r2.cloudflarestorage.com"
        : r2Settings.Endpoint;

    builder.Services.Configure<CloudflareR2Settings>(options =>
    {
        options.AccountId = r2Settings.AccountId;
        options.AccessKeyId = r2Settings.AccessKeyId;
        options.SecretAccessKey = r2Settings.SecretAccessKey;
        options.BucketName = r2Settings.BucketName;
        options.PublicBaseUrl = r2Settings.PublicBaseUrl;
        options.Endpoint = endpoint;
    });
    builder.Services.AddSingleton<IAmazonS3>(_ =>
    {
        var credentials = new BasicAWSCredentials(r2Settings.AccessKeyId, r2Settings.SecretAccessKey);
        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = true,
            AuthenticationRegion = "auto"
        };

        return new AmazonS3Client(credentials, config);
    });
    builder.Services.AddScoped<ICloudinaryService, CloudflareR2StorageService>();
    Console.WriteLine("Cloudflare R2 storage is configured. New image uploads will use R2.");
}
else if (!string.IsNullOrWhiteSpace(cloudName) &&
    !string.IsNullOrWhiteSpace(apiKey) &&
    !string.IsNullOrWhiteSpace(apiSecret))
{
    builder.Services.AddSingleton(new Cloudinary(new Account(cloudName, apiKey, apiSecret)));
    builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();
}
else
{
    builder.Services.AddScoped<ICloudinaryService, MissingCloudinaryService>();
    Console.WriteLine("Cloudinary settings are missing. Image uploads are disabled until configuration is added.");
}

var googleClientId = GetConfiguredValue(builder.Configuration, "Authentication:Google:ClientId", "GOOGLE_CLIENT_ID");
var googleClientSecret = GetConfiguredValue(builder.Configuration, "Authentication:Google:ClientSecret", "GOOGLE_CLIENT_SECRET");

if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
            options.CallbackPath = new PathString("/signin-google");
            options.SaveTokens = true;
            options.AccessDeniedPath = "/Identity/Account/Login";
        });
}
else
{
    Console.WriteLine("Google OAuth settings are missing. Google sign-in is disabled.");
}

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.PostConfigure<EmailSettings>(settings =>
{
    settings.SmtpUser = string.IsNullOrWhiteSpace(settings.SmtpUser)
        ? GetConfiguredValue(builder.Configuration, "EmailSettings:Username", "EmailSettings__Username") ?? string.Empty
        : settings.SmtpUser;

    settings.SmtpPass = string.IsNullOrWhiteSpace(settings.SmtpPass)
        ? GetConfiguredValue(builder.Configuration, "EmailSettings:Password", "EmailSettings__Password") ?? string.Empty
        : settings.SmtpPass;
});
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddScoped<IGamificationService, GamificationService>();
builder.Services.AddScoped<IDogProgressionService, DogProgressionService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedHost | ForwardedHeaders.XForwardedProto
});

app.UseStaticFiles();
app.UseRouting();
app.UseCookiePolicy();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Map}/{action=Index}/{id?}");

app.MapRazorPages();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    context.Database.Migrate();
    DbInitializer.Seed(context);
}

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    if (!await roleManager.RoleExistsAsync("Admin"))
    {
        await roleManager.CreateAsync(new IdentityRole("Admin"));
    }

    var adminEmail = "admin@doggydrop.app";
    var adminUser = await userManager.FindByEmailAsync(adminEmail);
    if (adminUser == null)
    {
        adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true
        };

        await userManager.CreateAsync(adminUser, "Admin123!");
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }
}

if (app.Configuration.GetValue<bool>("SeedTestUser:Enabled"))
{
    using var scope = app.Services.CreateScope();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var testEmail = app.Configuration["SeedTestUser:Email"];
    var testPassword = app.Configuration["SeedTestUser:Password"];

    if (!string.IsNullOrWhiteSpace(testEmail) && !string.IsNullOrWhiteSpace(testPassword))
    {
        var testUser = await userManager.FindByEmailAsync(testEmail);
        if (testUser == null)
        {
            testUser = new ApplicationUser
            {
                UserName = testEmail,
                Email = testEmail,
                EmailConfirmed = true,
                DisplayName = "Testni uporabnik"
            };

            var result = await userManager.CreateAsync(testUser, testPassword);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(error => error.Description));
                Console.WriteLine($"Test user was not created: {errors}");
            }
        }
    }
}

app.Run();

static string? GetConfiguredValue(IConfiguration configuration, params string[] keys)
{
    foreach (var key in keys)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static string? ResolveConnectionString(IConfiguration configuration)
{
    var explicitConnectionString = configuration.GetConnectionString("DefaultConnection");
    if (!string.IsNullOrWhiteSpace(explicitConnectionString))
    {
        return NormalizePostgresConnectionString(explicitConnectionString);
    }

    var databaseUrl = GetConfiguredValue(configuration, "DATABASE_URL");
    if (string.IsNullOrWhiteSpace(databaseUrl))
    {
        return null;
    }

    return NormalizePostgresConnectionString(databaseUrl);
}

static string NormalizePostgresConnectionString(string connectionString)
{
    if (connectionString.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
    {
        return connectionString;
    }

    if (!Uri.TryCreate(connectionString, UriKind.Absolute, out var databaseUri))
    {
        return connectionString;
    }

    var userInfoParts = databaseUri.UserInfo.Split(':', 2);
    var builder = new NpgsqlConnectionStringBuilder
    {
        Host = databaseUri.Host,
        Port = databaseUri.Port > 0 ? databaseUri.Port : 5432,
        Username = Uri.UnescapeDataString(userInfoParts.ElementAtOrDefault(0) ?? string.Empty),
        Password = Uri.UnescapeDataString(userInfoParts.ElementAtOrDefault(1) ?? string.Empty),
        Database = databaseUri.AbsolutePath.Trim('/'),
        SslMode = SslMode.Require
    };

    if (!string.IsNullOrWhiteSpace(databaseUri.Query))
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(databaseUri.Query);
        if (query.TryGetValue("sslmode", out var sslModeValue) &&
            Enum.TryParse<SslMode>(sslModeValue.ToString(), true, out var sslMode))
        {
            builder.SslMode = sslMode;
        }
    }

    return builder.ConnectionString;
}
