using DoggyDrop.Data;
using DoggyDrop.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class DatabaseMigrationCommandTests
{
    [Theory]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "migrate" }, true)]
    [InlineData(new[] { "MIGRATE" }, true)]
    [InlineData(new[] { "migrate", "extra" }, false)]
    [InlineData(new[] { "reconcile-achievements" }, false)]
    public void CommandDispatch_IsExplicitOnly(string[] args, bool expected)
    {
        Assert.Equal(expected, DatabaseMigrationCommand.IsRequested(args));
        Assert.False(DatabaseMigrationCommand.IsRequested(args) && AchievementReconciliationCommand.IsRequested(args));
    }

    [Fact]
    public async Task DatabaseResolutionFailure_ExitsNonzeroWithoutReconciliation()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<ApplicationDbContext>(_ => throw new InvalidOperationException("Simulated resolution failure."))
            .BuildServiceProvider();

        Assert.Equal(1, await DatabaseMigrationCommand.RunAsync(services));
    }
}
