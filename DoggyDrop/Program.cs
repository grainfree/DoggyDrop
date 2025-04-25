using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using DoggyDrop.Models;
using CloudinaryDotNet;
using DoggyDrop.Services;

var builder = WebApplication.CreateBuilder(args);

// 🔍 Diagnostika povezave z bazo
Console.WriteLine("📡 Connection string: " + builder.Configuration.GetConnectionString("DefaultConnection"));

// 🔌 Povezava z bazo
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 🔐 Identity in vloge
builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<UserManager<ApplicationUser>>();
builder.Services.AddScoped<SignInManager<ApplicationUser>>();

// 📦 Cloudinary servis
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();

// ✅ Preberi okoljske spremenljivke
var cloudName = builder.Configuration["Cloudinary__CloudName"];
var apiKey = builder.Configuration["Cloudinary__ApiKey"];
var apiSecret = builder.Configuration["Cloudinary__ApiSecret"];


if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
{
    throw new Exception("❌ Cloudinary environment variables are missing or invalid!");
}

// 🌩️ Diagnostika Cloudinary nastavitev
Console.WriteLine("🌩️ Cloudinary config check:");
Console.WriteLine($"CloudName: {cloudName}");
Console.WriteLine($"ApiKey: {apiKey}");
Console.WriteLine($"ApiSecret: {apiSecret}");

var cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
builder.Services.AddSingleton(cloudinary);

// 🌐 MVC + Razor Pages
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

// 🛑 Globalni handler za napake
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// 🗺️ Privzeta ruta
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

// 👑 Ustvari admina, če še ne obstaja
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
