using Backend.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Backend.Services;

public class SectorSnapshotJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SectorSnapshotJob> _logger;

    public SectorSnapshotJob(IServiceScopeFactory scopeFactory, ILogger<SectorSnapshotJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParkingContext>();

        var snapshotBucketSec = 3;
        var nowTs = DateTime.UtcNow;
        var bucketSecond = nowTs.Second - (nowTs.Second % snapshotBucketSec);
        var bucketTs = new DateTime(
            nowTs.Year,
            nowTs.Month,
            nowTs.Day,
            nowTs.Hour,
            nowTs.Minute,
            bucketSecond,
            DateTimeKind.Utc);

        var sectorIds = await db.Spots
            .Select(s => s.SectorId)
            .Distinct()
            .ToListAsync(context.CancellationToken);

        foreach (var sectorId in sectorIds)
        {
            try
            {
                var occupiedCount = await db.Spots
                    .Where(s => s.SectorId == sectorId && s.CurrentState == "OCCUPIED")
                    .CountAsync(context.CancellationToken);
                var total = await db.Spots
                    .Where(s => s.SectorId == sectorId)
                    .CountAsync(context.CancellationToken);
                var freeCount = total - occupiedCount;
                var occupancyRate = total > 0 ? (decimal)occupiedCount / (decimal)total : 0;

                // Use upsert SQL to avoid race conditions
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO ""SectorSnapshots"" (""Ts"", ""SectorId"", ""OccupiedCount"", ""FreeCount"", ""OccupancyRate"")
                    VALUES ({bucketTs}, {sectorId}, {occupiedCount}, {freeCount}, {occupancyRate})
                    ON CONFLICT (""Ts"", ""SectorId"") DO UPDATE SET
                        ""OccupiedCount"" = EXCLUDED.""OccupiedCount"",
                        ""FreeCount"" = EXCLUDED.""FreeCount"",
                        ""OccupancyRate"" = EXCLUDED.""OccupancyRate""
                ", context.CancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to update snapshot for sector {SectorId}: {Message}", sectorId, ex.Message);
                // Continue to next sector even if this one fails
            }
        }

        _logger.LogDebug("Sector snapshots updated at {BucketTs}", bucketTs);
    }
}
