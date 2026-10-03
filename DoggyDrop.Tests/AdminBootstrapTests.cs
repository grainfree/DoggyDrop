using System.Collections.Concurrent;
using System.Data.Common;
using System.Text.RegularExpressions;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class AdminBootstrapTests
{
    // Generated per fixture; never configuration defaults for the application.
    private static string NewPassword() => "Synthetic-" + Guid.NewGuid().ToString("N") + "!aA7";
    private static Dictionary<string, string?> Config(string password) => new()
    {
        [AdminBootstrap.EmailKey] = "initial-admin@example.invalid",
        [AdminBootstrap.PasswordKey] = password
    };

    [Theory]
    [InlineData("Development")] [InlineData("Test")] [InlineData("Production")]
    public async Task MissingConfigurationSeedsRoleOnlyAndAllowsStartup(string environment)
    {
        await using var f = await Fixture.Create(environment, new());
        Assert.Equal(AdminBootstrapOutcome.NotConfigured, await f.Run());
        await using var scope = f.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Users.ToListAsync());
        Assert.Empty(await db.UserRoles.ToListAsync());
        Assert.Equal("Admin", (await db.Roles.SingleAsync()).Name);
        Assert.Equal(AdminBootstrapOutcome.NotConfigured, await f.Run());
        Assert.Contains("bootstrap skipped", f.Log.Text);
    }

    [Theory]
    [InlineData("Development")] [InlineData("Test")] [InlineData("Production")]
    public async Task ExplicitConfigurationCreatesOneInitialAdminAndNeverResetsIt(string environment)
    {
        var password = NewPassword();
        await using var f = await Fixture.Create(environment, Config(password));
        Assert.Equal(AdminBootstrapOutcome.Created, await f.Run());
        string? originalHash;
        await using (var scope = f.Host.Services.CreateAsyncScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.FindByEmailAsync("initial-admin@example.invalid");
            Assert.NotNull(user);
            Assert.True(await manager.CheckPasswordAsync(user, password));
            Assert.True(await manager.IsInRoleAsync(user, "Admin"));
            Assert.True(user.EmailConfirmed);
            originalHash = user.PasswordHash;
        }
        f.Configuration[AdminBootstrap.PasswordKey] = NewPassword();
        Assert.Equal(AdminBootstrapOutcome.ExistingAdministrator, await f.Run());
        await using var check = f.Host.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var retained = await db.Users.SingleAsync();
        Assert.True(originalHash == retained.PasswordHash);
        Assert.Single(await db.UserRoles.ToListAsync());
        Assert.False(f.Log.Text.Contains(password, StringComparison.Ordinal));
        Assert.False(f.Log.Text.Contains(originalHash!, StringComparison.Ordinal));
        Assert.False(f.Log.Text.Contains(retained.Email!, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Development", false)] [InlineData("Test", false)] [InlineData("Production", false)]
    [InlineData("Development", true)] [InlineData("Test", true)] [InlineData("Production", true)]
    public async Task AnyExistingAdminMakesBootstrapConfigurationUnnecessary(string environment, bool partialConfiguration)
    {
        var configuration = partialConfiguration ? new Dictionary<string, string?> { [AdminBootstrap.EmailKey] = "another@example.invalid" } : new();
        await using var f = await Fixture.Create(environment, configuration);
        var hash = await f.SeedAccount(admin: true);
        Assert.Equal(AdminBootstrapOutcome.ExistingAdministrator, await f.Run());
        await using var scope = f.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(hash == (await db.Users.SingleAsync()).PasswordHash);
        Assert.Single(await db.UserRoles.ToListAsync());
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public async Task IncompleteInvalidOrWeakConfigurationFailsWithoutFallback(int scenario)
    {
        var password = NewPassword(); var configuration = Config(password);
        switch (scenario)
        {
            case 0: configuration.Remove(AdminBootstrap.EmailKey); break;
            case 1: configuration.Remove(AdminBootstrap.PasswordKey); break;
            case 2: configuration[AdminBootstrap.EmailKey] = " "; break;
            case 3: configuration[AdminBootstrap.EmailKey] = "not-an-email"; break;
            case 4: configuration[AdminBootstrap.PasswordKey] = "short"; break;
            case 5: configuration[AdminBootstrap.PasswordKey] = new string('a', 24); break;
        }
        await using var f = await Fixture.Create("Production", configuration);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        Assert.Contains(AdminBootstrap.EmailKey, error.Message);
        Assert.Contains(AdminBootstrap.PasswordKey, error.Message);
        Assert.Null(error.InnerException);
        Assert.False(error.ToString().Contains(password, StringComparison.Ordinal));
        Assert.False(f.Log.Text.Contains(password, StringComparison.Ordinal));
        await f.AssertNoAccount();
    }

    [Fact]
    public async Task ExistingOrdinaryAccountIsNeitherPromotedNorReset()
    {
        await using var f = await Fixture.Create("Production", Config(NewPassword()));
        var hash = await f.SeedAccount(admin: false, email: "initial-admin@example.invalid");
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        await using var scope = f.Host.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.True(hash == (await db.Users.SingleAsync()).PasswordHash);
        Assert.Empty(await db.UserRoles.ToListAsync());
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task ValidatorDescriptionsAndExceptionsCannotLeakSuppliedSecrets(bool throwException)
    {
        var password = NewPassword();
        await using var f = await Fixture.Create("Production", Config(password), new RejectingValidator(throwException));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        Assert.Null(error.InnerException);
        Assert.False(error.ToString().Contains(password, StringComparison.Ordinal));
        Assert.False(f.Log.Text.Contains(password, StringComparison.Ordinal));
        await f.AssertNoAccount();
    }

    [Fact]
    public async Task FailedRoleGrantRollsBackAccountCreation()
    {
        await using var f = await Fixture.Create("Production", Config(NewPassword()));
        f.Fault.Table = "AspNetUserRoles";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        await f.AssertNoAccount();
    }

    [Fact]
    public async Task FailedRoleCreationIsNotSilentlyIgnored()
    {
        await using var f = await Fixture.Create("Production", Config(NewPassword()));
        f.Fault.Table = "AspNetRoles";
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Run());
        await f.AssertNoAccount();
    }

    [Fact]
    public void ApplicationWiresExplicitBootstrapWithoutLiteralCredentialCreation()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "DoggyDrop.sln"))) root = root.Parent;
        Assert.NotNull(root);
        var app = Path.Combine(root.FullName, "DoggyDrop");
        var program = File.ReadAllText(Path.Combine(app, "Program.cs"));
        Assert.Contains("await AdminBootstrap.RunAsync(app.Services, app.Configuration);", program);
        Assert.DoesNotContain("CreateAsync(adminUser", program);
        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) &&
                        !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)))
        {
            // Structural guard, without embedding the exposed credential or its hash in tests.
            Assert.False(Regex.IsMatch(File.ReadAllText(file), "CreateAsync\\([^,\\r\\n]+,\\s*\"[^\"\\r\\n]+\"\\)"),
                "Application account creation must not use a literal password: " + Path.GetRelativePath(app, file));
        }
    }

    private sealed class RejectingValidator(bool throwException) : IPasswordValidator<ApplicationUser>
    {
        public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
        {
            if (throwException) throw new InvalidOperationException(password);
            return Task.FromResult(IdentityResult.Failed(new IdentityError { Code = "SyntheticRejected", Description = password! }));
        }
    }

    private sealed class InsertFault : DbCommandInterceptor
    {
        public string? Table { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Table != null && command.CommandText.Contains("INSERT INTO \"" + Table + "\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Synthetic database insert failure.");
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class CaptureLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> entries = new();
        public string Text => string.Join('\n', entries);
        public ILogger CreateLogger(string categoryName) => new Capture(entries);
        public void Dispose() { }
        private sealed class Capture(ConcurrentQueue<string> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
                => entries.Enqueue(formatter(state, exception) + (exception?.ToString() ?? ""));
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public IHost Host { get; }
        public IConfiguration Configuration { get; }
        public CaptureLogs Log { get; }
        public InsertFault Fault { get; }
        private Fixture(SqliteConnection connection, IHost host, IConfiguration configuration, CaptureLogs log, InsertFault fault)
            => (this.connection, Host, Configuration, Log, Fault) = (connection, host, configuration, log, fault);

        public static async Task<Fixture> Create(string environment, Dictionary<string, string?> configuration,
            IPasswordValidator<ApplicationUser>? validator = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
                { DisableDefaults = true, EnvironmentName = environment });
            builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(configuration);
            var log = new CaptureLogs(); var fault = new InsertFault();
            builder.Services.AddLogging(b => b.ClearProviders().AddProvider(log));
            builder.Services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(connection).AddInterceptors(fault));
            builder.Services.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
            if (validator != null) builder.Services.AddSingleton(validator);
            var host = builder.Build(); await host.StartAsync();
            var f = new Fixture(connection, host, builder.Configuration, log, fault);
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();
            return f;
        }

        public Task<AdminBootstrapOutcome> Run() => AdminBootstrap.RunAsync(Host.Services, Configuration);
        public async Task<string?> SeedAccount(bool admin, string email = "existing-admin@example.invalid")
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = email, Email = email };
            Assert.True((await users.CreateAsync(user, NewPassword())).Succeeded);
            if (admin)
            {
                var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
                Assert.True((await roles.CreateAsync(new IdentityRole("Admin"))).Succeeded);
                Assert.True((await users.AddToRoleAsync(user, "Admin")).Succeeded);
            }
            return user.PasswordHash;
        }
        public async Task AssertNoAccount()
        {
            await using var scope = Host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Empty(await db.Users.ToListAsync()); Assert.Empty(await db.UserRoles.ToListAsync());
        }
        public async ValueTask DisposeAsync()
        {
            await Host.StopAsync(); Host.Dispose(); await connection.DisposeAsync();
        }
    }
}
