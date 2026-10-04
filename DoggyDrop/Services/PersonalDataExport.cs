using System.Security.Claims;
using System.Buffers;
using System.Text.Json;
using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class PersonalDataExport(ApplicationDbContext db)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(string userId, Stream output, CancellationToken ct = default)
    {
        // A consistent snapshot, with one streamed projection per collection (no per-walk queries).
        await using var transaction = await db.Database.BeginTransactionAsync(db.Database.IsNpgsql()
            ? System.Data.IsolationLevel.RepeatableRead : System.Data.IsolationLevel.Serializable, ct);
        // JsonSerializer may flush its writer synchronously. Buffer bounded batches in
        // memory, then write asynchronously; never enable synchronous HTTP response I/O.
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true });
        writer.WriteStartObject();
        await Rows("BinMaintenanceContributions", db.BinContributions.Where(c => c.SubmittedByUserId == userId).OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.BinId, c.Type, c.Reason, c.Status, c.Description, c.ProposedLatitude, c.ProposedLongitude, c.PossibleDuplicateBinId, c.CreatedAt, c.ReviewedAt }));
        await Rows("InfrastructureConfirmations", db.InfrastructureConfirmations.AsNoTracking().Where(c => c.UserId == userId).OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Type, c.TrashBinId, c.WaterPointId, c.CreatedAt }));
        writer.WriteNumber("FormatVersion", 1);
        writer.WriteString("ExportedAtUtc", DateTime.UtcNow);
        writer.WriteString("Scope", "Podatki trenutnega računa v DoggyDrop. Fotografije so navedene kot povezave, ne kot datoteke. Varnostne skrivnosti in podatki ponudnikov zunaj aplikacije niso vključeni.");
        writer.WritePropertyName("Account");
        var account = await db.Users.Where(x => x.Id == userId).Select(x => new {
            x.Id, x.UserName, x.Email, x.EmailConfirmed, x.PhoneNumber, x.PhoneNumberConfirmed,
            x.TwoFactorEnabled, x.DisplayName, x.ProfileImageUrl
        }).SingleAsync(ct);
        JsonSerializer.Serialize(writer, account, JsonOptions);

        await Rows("ExternalLogins", db.UserLogins.Where(x => x.UserId == userId)
            .Select(x => new { x.LoginProvider, x.ProviderDisplayName, x.ProviderKey }));
        // Explicit allowlist: arbitrary claims or token collections can contain credentials.
        await Rows("ProfileClaims", db.UserClaims.Where(x => x.UserId == userId &&
            (x.ClaimType == ClaimTypes.Name || x.ClaimType == ClaimTypes.GivenName || x.ClaimType == ClaimTypes.Surname || x.ClaimType == ClaimTypes.Email))
            .Select(x => new { x.ClaimType, x.ClaimValue }));
        await Rows("Roles", db.UserRoles.Where(x => x.UserId == userId).Join(db.Roles, x => x.RoleId, x => x.Id, (x,r) => new { r.Name }));
        await Rows("Dogs", db.Dogs.Where(x => x.OwnerId == userId).OrderBy(x => x.Id).Select(x => new {
            x.Id, x.Name, x.Breed, x.AgeYears, x.Gender, x.Size, x.Character, x.PhotoUrl, x.MapIconKey,
            x.NearbyVisibility, x.LastKnownLatitude, x.LastKnownLongitude, x.LastLocationUpdatedAt, x.CreatedAt
        }));
        await Rows("Walks", db.Walks.Where(x => x.OwnerId == userId).OrderBy(x => x.Id).Select(x => new {
            x.Id, x.DogId, x.PlannedWalkId, x.StartedAt, x.EndedAt, x.DistanceMeters, x.UsedBinsCount, x.Status
        }));
        await Rows("WalkPoints", db.WalkPoints.Where(x => x.Walk!.OwnerId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WalkId, x.Latitude, x.Longitude, x.RecordedAt }));
        await Rows("WalkPhotos", db.WalkPhotos.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WalkId, x.ImageUrl, x.Caption, x.CreatedAt, x.PlannedWalkStopId }));
        await Rows("SavedPlaces", db.SavedPlaces.Where(x => x.UserId == userId).OrderBy(x => x.PlaceId)
            .Select(x => new { x.PlaceId, x.SavedAt }));
        // Minimal references only; never traverse DataSource or Admin verification metadata.
        await Rows("PublicPlaceReferences", db.Places.ForPublicDetails().Where(x => db.SavedPlaces.Any(s => s.UserId == userId && s.PlaceId == x.Id))
            .Select(x => new { x.Id, x.Name, x.Category, x.Address }));
        await Rows("PrivacyZone", db.PrivacyZones.Where(x => x.UserId == userId)
            .Select(x => new { x.Latitude, x.Longitude, x.RadiusMeters }));
        await Rows("NearbyDiscoveryPreference", db.NearbyDiscoveryPreferences.Where(x => x.UserId == userId)
            .Select(x => new { x.Latitude, x.Longitude, x.RadiusMeters, x.BinsEnabled, x.EnabledAt }));
        await Rows("BinContributions", db.TrashBins.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Name, x.Latitude, x.Longitude, x.ImageUrl, x.DateAdded, x.IsApproved, x.ApprovedAt }));
        await Rows("Friendships", db.Friendships.Where(x => x.RequesterId == userId || x.AddresseeId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.RequesterId, x.AddresseeId, x.Status, x.CreatedAt, x.RespondedAt }));
        await Rows("Notifications", db.UserNotifications.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.Type, Title = NotificationPrivacy.Title(x.Type, x.Title), Body = NotificationPrivacy.Body(x.Type, x.Body), LinkUrl = NotificationPrivacy.Link(x.Type, x.LinkUrl), x.IsRead, x.CreatedAt, x.ReadAt }));
        await Rows("PlaydateRequests", db.PlaydateRequests.Where(x => x.OwnerId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.DogId, x.LocationLabel, x.PreferredAt, x.SizePreference, x.EnergyLevel, x.Note, x.Status, x.CreatedAt }));
        await Rows("PlaydateInterests", db.PlaydateInterests.Where(x => x.OwnerId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.PlaydateRequestId, x.DogId, x.Message, x.Status, x.CreatedAt }));
        await Rows("WalkReactions", db.WalkReactions.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WalkId, x.ReactionType, x.CreatedAt }));
        await Rows("WalkPhotoReactions", db.WalkPhotoReactions.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WalkPhotoId, x.ReactionType, x.CreatedAt }));
        await Rows("WalkComments", db.WalkComments.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WalkId, x.Body, x.CreatedAt, x.IsDeleted }));
        await Rows("ParkVisits", db.DogParkVisits.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.DogId, x.ParkName, x.Area, x.Address, x.PlaceKey, x.Latitude, x.Longitude, x.VisitedAt }));
        await Rows("PlannedWalks", db.PlannedWalks.Where(x => x.OwnerId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.DogId, x.Title, x.AreaKey, x.AreaName, x.TargetDistanceKm, x.EstimatedDistanceKm,
                x.EstimatedMinutes, x.IncludeBins, x.IncludePark, x.IncludeWater, x.IncludeDogFriendly, x.CreatedAt, x.UsedAt }));
        await Rows("PlannedWalkStops", db.PlannedWalkStops.Where(x => x.PlannedWalk!.OwnerId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.PlannedWalkId, x.Order, x.Name, x.Type, x.Label, x.Reason, x.Latitude, x.Longitude }));
        await Rows("PlannedWalkRoutePoints", db.PlannedWalkRoutePoints.Where(x => x.PlannedWalk!.OwnerId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.PlannedWalkId, x.Order, x.Latitude, x.Longitude }));
        await Rows("WalkStopCompletions", db.WalkStopCompletions.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.WalkId, x.PlannedWalkStopId, x.CompletedAt }));
        await Rows("Achievements", db.UserAchievements.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.AchievementKey, x.UnlockedAt, x.SourceType, x.SourceId, x.CreatedAt }));
        await Rows("FounderBadges", db.FounderBadges.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.AreaKey, x.AreaName, x.BadgeType, x.TrashBinId, x.UnlockedAt }));
        await Rows("GamificationProfile", db.UserGamificationProfiles.Where(x => x.UserId == userId)
            .Select(x => new { x.TotalXp, x.Level, x.Title, x.CurrentStreakDays, x.LongestStreakDays, x.LastDailyLoginDate, x.CreatedAt, x.UpdatedAt }));
        await Rows("XpEvents", db.UserXpEvents.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.ActivityType, x.XpAmount, x.ReferenceType, x.ReferenceId, x.Description, x.OccurredAt }));
        await Rows("Streaks", db.UserStreaks.Where(x => x.UserId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.StreakType, x.CurrentDays, x.LongestDays, x.FreezeCredits, x.LastActivityDate, x.LastFreezeUsedDate, x.CreatedAt, x.UpdatedAt }));
        await Rows("DogProgression", db.DogProgressionProfiles.Where(x => x.Dog!.OwnerId == userId)
            .Select(x => new { x.DogId, x.TotalXp, x.Level, x.DogClass, x.Adventure, x.Social, x.Forest, x.City, x.Water, x.Speed, x.CreatedAt, x.UpdatedAt }));
        await Rows("DogXpEvents", db.DogXpEvents.Where(x => x.Dog!.OwnerId == userId).OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.DogId, x.ActivityType, x.XpAmount, x.ReferenceType, x.ReferenceId, x.Description, x.OccurredAt }));
        writer.WriteEndObject();
        await FlushAsync();
        await transaction.CommitAsync(ct);

        async Task Rows<T>(string name, IQueryable<T> query)
        {
            writer.WritePropertyName(name);
            writer.WriteStartArray();
            var count = 0;
            await foreach (var row in query.AsAsyncEnumerable().WithCancellation(ct))
            {
                JsonSerializer.Serialize(writer, row, JsonOptions);
                if (++count % 256 == 0) await FlushAsync();
            }
            writer.WriteEndArray();
            await FlushAsync();
        }

        async Task FlushAsync()
        {
            writer.Flush();
            await output.WriteAsync(buffer.WrittenMemory, ct);
            buffer.Clear();
        }
    }
}
