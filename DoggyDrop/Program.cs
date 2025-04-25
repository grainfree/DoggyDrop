using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI;
using Microsoft.Extensions.Configuration;
using DoggyDrop.Models;
using CloudinaryDotNet;
using DoggyDrop.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(); // ✅ DODAJ TO VRSICO

// 🔍 Izpiši connection string za diagnostiko
Console.WriteLine("📡 Connection string: " + builder.Configuration.GetConnectionString("DefaultConnection"));

// 🔌 Database povezava
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// 🔐 Identity + roles
builder.Services.AddDefaultIdentity<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddScoped<UserManager<ApplicationUser>>();
builder.Services.AddScoped<SignInManager<ApplicationUser>>();

// 📦 Cloudinary servis
builder.Services.AddScoped<ICloudinaryService, CloudinaryService>();


// ✅ Varen način pridobivanja Cloudinary nastavitev
var configSection = builder.Configuration.GetSection("Cloudinary");
if (!configSection.Exists())
{
    throw new Exception("❌ Cloudinary settings are missing! Check environment variables or appsettings.json.");
}

var cloudinarySettings = configSection.Get<CloudinarySettings>();
if (cloudinarySettings == null || string.IsNullOrEmpty(cloudinarySettings.CloudName))
{
    throw new Exception("❌ Cloudinary configuration is invalid or incomplete!");
}

var cloudinary = new Cloudinary(new Account(
    cloudinarySettings.CloudName,
    cloudinarySettings.ApiKey,
    cloudinarySettings.ApiSecret
));

builder.Services.AddSingleton(cloudinary);

// 🌐 MVC
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

// 🛑 Global error handler
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

// 👑 Dodaj admin uporabnika, če še ne obstaja
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
