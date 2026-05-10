using System.Text.Json;
using Backend.Data;
using Backend.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

public interface IParkingIngestService
{
    Task HandleEventAsync(EventRequest request, CancellationToken cancellationToken = default);
    Task HandleGatewayStatusAsync(GatewayStatusRequest request, CancellationToken cancellationToken = default);
}

public class ParkingIngestService : IParkingIngestService
{
    private readonly ParkingContext _db;
    private readonly int _stuckThresholdSec;

    public ParkingIngestService(ParkingContext db, IConfiguration configuration)
    {
        _db = db;
        _stuckThresholdSec = int.TryParse(Environment.GetEnvironmentVariable("STUCK_SECONDS"), out var st)
            ? st
            : configuration.GetValue<int?>("StuckSeconds") ?? 3600;
    }

    public async Task HandleEventAsync(EventRequest request, CancellationToken cancellationToken = default)
    {
        var exists = await _db.SpotEvents.FindAsync(new object?[] { request.EventId }, cancellationToken);
        if (exists != null)
        {
            return;
        }

        var se = new Models.SpotEvent
        {
            EventId = request.EventId,
            Ts = request.Ts,
            SectorId = request.SectorId,
            SpotId = request.SpotId,
            State = request.State,
            RawPayloadJson = JsonSerializer.Serialize(request)
        };
        _db.SpotEvents.Add(se);

        var spot = await _db.Spots.FindAsync(new object?[] { request.SpotId }, cancellationToken);
        if (spot == null)
        {
            spot = new Models.Spot
            {
                SpotId = request.SpotId,
                SectorId = request.SectorId,
                CurrentState = request.State,
                LastChangeTs = request.Ts,
                LastEventId = request.EventId
            };
            _db.Spots.Add(spot);
        }
        else
        {
            var prevState = spot.CurrentState;
            if (prevState != request.State)
            {
                spot.CurrentState = request.State;
                spot.LastChangeTs = request.Ts;

                var openIncidents = await _db.Incidents
                    .Where(i => i.SpotId == request.SpotId && i.Status == "open")
                    .ToListAsync(cancellationToken);
                foreach (var inc in openIncidents)
                {
                    if (inc.Type == "STUCK_OCCUPIED" || inc.Type == "STUCK_FREE" || inc.Type == "FLAPPING")
                    {
                        inc.Status = "closed";
                        inc.TsClose = DateTime.UtcNow;
                        _db.Incidents.Update(inc);
                    }
                }
            }
            spot.LastEventId = request.EventId;
            _db.Spots.Update(spot);
        }

        var flappingWindowSec = 30;
        var flappingThreshold = 4;
        var windowStart = request.Ts.AddSeconds(-flappingWindowSec);
        var recentCount = await _db.SpotEvents
            .Where(e => e.SpotId == request.SpotId && e.Ts >= windowStart)
            .CountAsync(cancellationToken);
        recentCount += 1;
        if (recentCount >= flappingThreshold)
        {
            var existsInc = await _db.Incidents
                .Where(i => i.SpotId == request.SpotId && i.Type == "FLAPPING" && i.Status == "open")
                .FirstOrDefaultAsync(cancellationToken);
            if (existsInc == null)
            {
                var inc = new Models.Incident
                {
                    Id = Guid.NewGuid(),
                    TsOpen = DateTime.UtcNow,
                    Type = "FLAPPING",
                    Severity = 2,
                    SectorId = request.SectorId,
                    SpotId = request.SpotId,
                    EvidenceJson = JsonSerializer.Serialize(new { recentCount, windowSec = flappingWindowSec }),
                    Status = "open"
                };
                await IncidentWriter.AddIfNotExistsAsync(_db, inc, cancellationToken);
            }
        }

        if (spot.LastChangeTs != null && spot.LastEventId != null)
        {
            var age = DateTime.UtcNow - spot.LastChangeTs.Value.ToUniversalTime();
            if (age.TotalSeconds >= _stuckThresholdSec)
            {
                var type = spot.CurrentState == "OCCUPIED" ? "STUCK_OCCUPIED" : "STUCK_FREE";
                var existsInc = await _db.Incidents
                    .Where(i => i.SpotId == request.SpotId && i.Type == type && i.Status == "open")
                    .FirstOrDefaultAsync(cancellationToken);
                if (existsInc == null)
                {
                    var inc = new Models.Incident
                    {
                        Id = Guid.NewGuid(),
                        TsOpen = DateTime.UtcNow,
                        Type = type,
                        Severity = 3,
                        SectorId = request.SectorId,
                        SpotId = request.SpotId,
                        EvidenceJson = JsonSerializer.Serialize(new { lastChange = spot.LastChangeTs, now = DateTime.UtcNow }),
                        Status = "open"
                    };
                    await IncidentWriter.AddIfNotExistsAsync(_db, inc, cancellationToken);
                }
            }
        }

        var minuteTs = DateTime.UtcNow;
        minuteTs = new DateTime(minuteTs.Year, minuteTs.Month, minuteTs.Day, minuteTs.Hour, minuteTs.Minute, 0, DateTimeKind.Utc);
        var occupiedCount = await _db.Spots
            .Where(s => s.SectorId == request.SectorId && s.CurrentState == "OCCUPIED")
            .CountAsync(cancellationToken);
        var total = await _db.Spots
            .Where(s => s.SectorId == request.SectorId)
            .CountAsync(cancellationToken);
        var freeCount = total - occupiedCount;
        var occupancyRate = total > 0 ? (decimal)occupiedCount / (decimal)total : 0;
        var existingSnapshot = await _db.SectorSnapshots
            .Where(s => s.SectorId == request.SectorId && s.Ts == minuteTs)
            .FirstOrDefaultAsync(cancellationToken);
        if (existingSnapshot == null)
        {
            _db.SectorSnapshots.Add(new Models.SectorSnapshot
            {
                Ts = minuteTs,
                SectorId = request.SectorId,
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
            _db.SectorSnapshots.Update(existingSnapshot);
        }

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task HandleGatewayStatusAsync(GatewayStatusRequest request, CancellationToken cancellationToken = default)
    {
        var gs = await _db.GatewayStatuses.FindAsync(new object?[] { request.SectorId }, cancellationToken);
        if (gs == null)
        {
            gs = new Models.GatewayStatus
            {
                SectorId = request.SectorId,
                Status = request.Status,
                LastSeen = request.Ts ?? DateTime.UtcNow
            };
            _db.GatewayStatuses.Add(gs);
        }
        else
        {
            gs.Status = request.Status;
            gs.LastSeen = request.Ts ?? DateTime.UtcNow;
            _db.GatewayStatuses.Update(gs);
        }
        await _db.SaveChangesAsync(cancellationToken);
    }
}
