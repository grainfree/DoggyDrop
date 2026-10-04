using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class PrivacyLaunchLifecycleTests
{
    [Fact] public Task DeletionPreservesHistoricalEvidenceAndRemovesPrivateGraph() => CheckDeletion();
    // Reused by the isolated loopback PostgreSQL runner; never takes application configuration.
    internal static async Task CheckDeletion(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture = await PrivacyTestDatabase.Create(options);
        await PrivacyLifecycleTests.Seed(fixture.Db);
        var db=fixture.Db;
        var bin=await db.TrashBins.SingleAsync(b=>b.IsApproved);
        var water=new WaterPoint {Name="Synthetic water",IsApproved=true,Potability=WaterPotability.SourceReportedDrinking};
        db.WaterPoints.Add(water);
        db.InfrastructureConfirmations.AddRange(new(){UserId="alice",TrashBin=bin,Type=InfrastructureConfirmationType.TrashBinPresent,CreatedAt=DateTime.UtcNow,EvidenceVersion=bin.EvidenceVersion},
            new(){UserId="bob",WaterPoint=water,Type=InfrastructureConfirmationType.WaterPointWorking,CreatedAt=DateTime.UtcNow});
        db.BinContributions.Add(new(){Bin=bin,SubmittedByUserId="alice",Type=BinContributionType.Issue,Description="PRIVATE_SUBMITTED_NOTE",RequestId=Guid.NewGuid()});
        await db.SaveChangesAsync();
        // A formerly public object can lose approval but still have retained FK evidence.
        bin.IsApproved=false; await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var result=await new AccountDataDeletion(db,fixture.Users,new NoMedia()).DeleteAsync(await db.Users.SingleAsync(u=>u.Id=="alice"));
        Assert.True(result.Succeeded);db.ChangeTracker.Clear();
        Assert.False(await db.Users.AnyAsync(u=>u.Id=="alice"));
        Assert.False(await db.Dogs.AnyAsync(d=>d.OwnerId=="alice"));
        Assert.False(await db.Walks.AnyAsync(w=>w.OwnerId=="alice"));
        Assert.False(await db.PlannedWalks.AnyAsync(w=>w.OwnerId=="alice"));
        Assert.Single(await db.WalkPoints.ToListAsync()); // Bob's private point survives.
        Assert.Null((await db.InfrastructureConfirmations.SingleAsync(c=>c.TrashBinId==bin.Id)).UserId);
        Assert.Equal("bob",(await db.InfrastructureConfirmations.SingleAsync(c=>c.WaterPointId==water.Id)).UserId);
        var retained=await db.TrashBins.SingleAsync(b=>b.Id==bin.Id);Assert.Null(retained.UserId);Assert.False(retained.IsApproved);
        var contribution=await db.BinContributions.SingleAsync();Assert.Null(contribution.SubmittedByUserId);Assert.Null(contribution.Description);Assert.Equal(BinContributionStatus.Rejected,contribution.Status);
        Assert.Empty(await new InfrastructureTrust(db,TimeProvider.System).BinsAsync());
    }
    private sealed class NoMedia : IUserMediaCleanup {public Task CleanupAsync(IEnumerable<string?> urls)=>Task.CompletedTask;}
}
