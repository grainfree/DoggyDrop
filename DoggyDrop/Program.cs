using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using DoggyDrop.Models;
using CloudinaryDotNet;
using DoggyDrop.Services;

var builder = WebApplication.CreateBuilder(args);

// ✅ omogoči branje iz environment variables
builder.Configuration.AddEnvironmentVariables();

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

// ✅ Preberi okoljske spremenljivke neposredno
var cloudName = builder.Configuration["CLOUDINARY_CLOUD_NAME"];
var apiKey = builder.Configuration["CLOUDINARY_API_KEY"];
var apiSecret = builder.Configuration["CLOUDINARY_API_SECRET"];

if (string.IsNullOrEmpty(cloudName) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(apiSecret))
{
    throw new Exception("❌ Cloudinary environment variables are missing or invalid!");
}

// 🌩️ Diagnostika
Console.WriteLine("🌩️ Cloudinary config check:");
Console.WriteLine($"CloudName: {cloudName}");
Console.WriteLine($"ApiKey: {apiKey}");
Console.WriteLine($"ApiSecret: {apiSecret}");

// ✅ Najprej registriraj Cloudinary
var cloudinary = new Cloudinary(new Account(cloudName, apiKey, apiSecret));
builder.Services.AddSingleton(cloudinary);

// 📦 Potem pravilno registriraj CloudinaryService
builder.Services.AddScoped<ICloudinaryService>(provider =>
{
    var cloudinary = provider.GetRequiredService<Cloudinary>();
    return new CloudinaryService(cloudinary);
});

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
