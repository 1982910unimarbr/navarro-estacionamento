using Backend.Data;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace Backend.Services;

public class SpotAutoFreeJob : IJob
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SpotAutoFreeJob> _logger;
    private readonly int _occupiedHoldSec;

    public SpotAutoFreeJob(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<SpotAutoFreeJob> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _occupiedHoldSec = int.TryParse(Environment.GetEnvironmentVariable("OCCUPIED_HOLD_SEC"), out var hold)
            ? hold
            : configuration.GetValue<int?>("OccupiedHoldSeconds") ?? 10;
    }

    public async Task Execute(IJobExecutionContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParkingContext>();
        var now = DateTime.UtcNow;
        var cutoff = now.AddSeconds(-_occupiedHoldSec);

        var occupiedSpots = await db.Spots
            .Where(s => s.CurrentState == "OCCUPIED" && s.LastChangeTs != null && s.LastChangeTs <= cutoff)
            .ToListAsync(context.CancellationToken);

        if (occupiedSpots.Count == 0)
        {
            return;
        }

        foreach (var spot in occupiedSpots)
        {
            spot.CurrentState = "FREE";
            spot.LastChangeTs = now;
            db.Spots.Update(spot);
        }

        await db.SaveChangesAsync(context.CancellationToken);
        _logger.LogDebug("Auto-freed {Count} spots", occupiedSpots.Count);
    }
}
