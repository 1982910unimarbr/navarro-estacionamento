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
            var occupiedCount = await db.Spots
                .Where(s => s.SectorId == sectorId && s.CurrentState == "OCCUPIED")
                .CountAsync(context.CancellationToken);
            var total = await db.Spots
                .Where(s => s.SectorId == sectorId)
                .CountAsync(context.CancellationToken);
            var freeCount = total - occupiedCount;
            var occupancyRate = total > 0 ? (decimal)occupiedCount / (decimal)total : 0;

            var existingSnapshot = await db.SectorSnapshots
                .Where(s => s.SectorId == sectorId && s.Ts == bucketTs)
                .FirstOrDefaultAsync(context.CancellationToken);

            if (existingSnapshot == null)
            {
                db.SectorSnapshots.Add(new Models.SectorSnapshot
                {
                    Ts = bucketTs,
                    SectorId = sectorId,
                    OccupiedCount = occupiedCount,
                    FreeCount = freeCount,
                    OccupancyRate = occupancyRate
                });
            }
            else
            {
                existingSnapshot.OccupiedCount = occupiedCount;
                existingSnapshot.FreeCount = freeCount;
                existingSnapshot.OccupancyRate = occupancyRate;
                db.SectorSnapshots.Update(existingSnapshot);
            }
        }

        await db.SaveChangesAsync(context.CancellationToken);
        _logger.LogDebug("Sector snapshots updated at {BucketTs}", bucketTs);
    }
}
