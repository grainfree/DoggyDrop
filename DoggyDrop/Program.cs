using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using DoggyDrop.Models;
using CloudinaryDotNet;
using DoggyDrop.Services;

var builder = WebApplication.CreateBuilder(args);

// ✅ omogoči branje iz environment variables
builder.Configuration.AddEnvironmentVariables();

// 🔍 Izpiši povezavo do baze za preverjanje
Console.WriteLine("📡 Connection string: " + builder.Configuration.GetConnectionString("DefaultConnection"));

// 🔌 Povezava na bazo
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 🔐 Identity + roles
builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<UserManager<ApplicationUser>>();
builder.Services.AddScoped<SignInManager<ApplicationUser>>();

// ✅ Pravilno branje Cloudinary nastavitev
var cloudName = builder.Configuration["CLOUDINARY_CLOUD_NAME"];
var apiKey = builder.Configuration["CLOUDINARY_API_KEY"];
var apiSecret = builder.Configuration["CLOUDINARY_API_SECRET"];

// 🌩️ Izpis diagnostike
Console.WriteLine("🌩️ Cloudinary nastavitve:");
Console.WriteLine($"CLOUDINARY_CLOUD_NAME: {cloudName}");
Console.WriteLine($"CLOUDINARY_API_KEY: {apiKey}");
Console.WriteLine($"CLOUDINARY_API_SECRET: {apiSecret}");

// ✅ Če ni nastavljeno, failaj
if (string.IsNullOrWhiteSpace(cloudName) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(apiSecret))
{
    throw new Exception("❌ Cloudinary environment variables are missing or invalid!");
}

// ✅ Registriraj Cloudinary
var cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
builder.Services.AddSingleton(cloudinary);

// 📦 Registriraj CloudinaryService
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();

// ✅ Dodaj Google OAuth
var googleClientId = builder.Configuration["GOOGLE_CLIENT_ID"];
var googleClientSecret = builder.Configuration["GOOGLE_CLIENT_SECRET"];


if (string.IsNullOrWhiteSpace(googleClientId) || string.IsNullOrWhiteSpace(googleClientSecret))
{
    throw new Exception("❌ Google OAuth credentials are missing in appsettings.json!");
}

builder.Services.AddAuthentication()
    .AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
    });

// 🌐 MVC
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

// 🛑 Globalni error handler
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

// 📂 Static files, routing, auth
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// 🗺️ Default route
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Map}/{action=Index}/{id?}");

app.MapRazorPages();

// 🌱 Inicializacija baze
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<ApplicationDbContext>();
    DbInitializer.Seed(context);
}

// 👑 Admin uporabnik
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

app.Run();
