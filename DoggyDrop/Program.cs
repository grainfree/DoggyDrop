using CloudinaryDotNet;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

var cloudName = GetConfiguredValue(builder.Configuration, "Cloudinary:CloudName", "CLOUDINARY_CLOUD_NAME");
var apiKey = GetConfiguredValue(builder.Configuration, "Cloudinary:ApiKey", "CLOUDINARY_API_KEY");
var apiSecret = GetConfiguredValue(builder.Configuration, "Cloudinary:ApiSecret", "CLOUDINARY_API_SECRET");

if (!string.IsNullOrWhiteSpace(cloudName) &&
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
builder.Services.AddTransient<IEmailSender, EmailSender>();
builder.Services.AddScoped<INotificationService, NotificationService>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

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
        return explicitConnectionString;
    }

    var databaseUrl = GetConfiguredValue(configuration, "DATABASE_URL");
    if (string.IsNullOrWhiteSpace(databaseUrl))
    {
        return null;
    }

    if (databaseUrl.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
    {
        return databaseUrl;
    }

    if (!Uri.TryCreate(databaseUrl, UriKind.Absolute, out var databaseUri))
    {
        return databaseUrl;
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
