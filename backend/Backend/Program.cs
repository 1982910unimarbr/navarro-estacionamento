Cotnusing Microsoft.EntityFrameworkCore;
using Backend.Data;
using System.Text.Json;
using System;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.AddControllers();
// use minimal built-in OpenAPI helper
builder.Services.AddOpenApi();

var conn = builder.Configuration.GetConnectionString("Default") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=postgres;Database=parking;Username=parking_user;Password=parking_pass";

builder.Services.AddDbContext<ParkingContext>(opt => opt.UseNpgsql(conn));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// internal endpoint: ingest events forwarded by MQTT bridge
app.MapPost("/api/v1/internal/events", async (ParkingContext db, HttpRequest req) =>
{
    using var doc = await JsonDocument.ParseAsync(req.Body);
    if (!doc.RootElement.TryGetProperty("eventId", out var evId)) return Results.BadRequest();
    var eventId = evId.GetString();
    if (string.IsNullOrEmpty(eventId)) return Results.BadRequest();
    var exists = await db.SpotEvents.FindAsync(new object[] { eventId });
    if (exists != null) return Results.Ok(new { ok = true, duplicate = true });

    var state = doc.RootElement.GetProperty("state").GetString();
    var sectorId = doc.RootElement.GetProperty("sectorId").GetString();
    var spotId = doc.RootElement.GetProperty("spotId").GetString();
    var ts = doc.RootElement.GetProperty("ts").GetDateTime();

    var se = new Backend.Models.SpotEvent { EventId = eventId, Ts = ts, SectorId = sectorId, SpotId = spotId, State = state, RawPayloadJson = doc.RootElement.GetRawText() };
    db.SpotEvents.Add(se);

    var spot = await db.Spots.FindAsync(spotId);
    if (spot == null)
    {
        // new spot: initialize lastChangeTs since this is the first known state
        spot = new Backend.Models.Spot { SpotId = spotId, SectorId = sectorId, CurrentState = state, LastChangeTs = ts, LastEventId = eventId };
        db.Spots.Add(spot);
    }
    else
    {
        var prevState = spot.CurrentState;
        // Only update LastChangeTs when the state actually changes (track transitions)
        if (prevState != state)
        {
            spot.CurrentState = state;
            spot.LastChangeTs = ts;
        }
        // always update last event id
        spot.LastEventId = eventId;
        db.Spots.Update(spot);
    }

    // basic incident detection
    // FLAPPING: many events for same spot within short window
    var flappingWindowSec = 30; // seconds
    var flappingThreshold = 4; // events
    var windowStart = ts.AddSeconds(-flappingWindowSec);
    var recentCount = await db.SpotEvents.Where(e => e.SpotId == spotId && e.Ts >= windowStart).CountAsync();
    recentCount += 1; // include current event
    if (recentCount >= flappingThreshold)
    {
        var existsInc = await db.Incidents.Where(i => i.SpotId == spotId && i.Type == "FLAPPING" && i.Status == "open").FirstOrDefaultAsync();
        if (existsInc == null)
        {
            var inc = new Backend.Models.Incident { TsOpen = DateTime.UtcNow, Type = "FLAPPING", Severity = 2, SectorId = sectorId, SpotId = spotId, EvidenceJson = JsonSerializer.Serialize(new { recentCount, windowSec = flappingWindowSec }), Status = "open" };
            db.Incidents.Add(inc);
        }
    }

    // STUCK detection: if spot has not changed for a long time compared to threshold
    var stuckThresholdSec = int.TryParse(Environment.GetEnvironmentVariable("STUCK_SECONDS"), out var st) ? st : 3600; // default 1h
    if (spot.LastChangeTs != null)
    {
        var age = DateTime.UtcNow - spot.LastChangeTs.Value.ToUniversalTime();
        if (age.TotalSeconds >= stuckThresholdSec)
        {
            var type = spot.CurrentState == "OCCUPIED" ? "STUCK_OCCUPIED" : "STUCK_FREE";
            var existsInc = await db.Incidents.Where(i => i.SpotId == spotId && i.Type == type && i.Status == "open").FirstOrDefaultAsync();
            if (existsInc == null)
            {
                var inc = new Backend.Models.Incident { TsOpen = DateTime.UtcNow, Type = type, Severity = 3, SectorId = sectorId, SpotId = spotId, EvidenceJson = JsonSerializer.Serialize(new { lastChange = spot.LastChangeTs, now = DateTime.UtcNow }), Status = "open" };
                db.Incidents.Add(inc);
            }
        }
    }

    // create/update sector snapshot (per-minute)
    var minuteTs = DateTime.UtcNow;
    minuteTs = new DateTime(minuteTs.Year, minuteTs.Month, minuteTs.Day, minuteTs.Hour, minuteTs.Minute, 0, DateTimeKind.Utc);
    var occupiedCount = await db.Spots.Where(s => s.SectorId == sectorId && s.CurrentState == "OCCUPIED").CountAsync();
    var total = await db.Spots.Where(s => s.SectorId == sectorId).CountAsync();
    var freeCount = total - occupiedCount;
    var occupancyRate = total > 0 ? (decimal)occupiedCount / (decimal)total : 0;
    var existingSnapshot = await db.SectorSnapshots.Where(s => s.SectorId == sectorId && s.Ts == minuteTs).FirstOrDefaultAsync();
    if (existingSnapshot == null)
    {
        db.SectorSnapshots.Add(new Backend.Models.SectorSnapshot { Ts = minuteTs, SectorId = sectorId, OccupiedCount = occupiedCount, FreeCount = freeCount, OccupancyRate = occupancyRate });
    }
    else
    {
        existingSnapshot.OccupiedCount = occupiedCount;
        existingSnapshot.FreeCount = freeCount;
        existingSnapshot.OccupancyRate = occupancyRate;
        db.SectorSnapshots.Update(existingSnapshot);
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
});

app.MapGet("/api/v1/map", async (ParkingContext db) =>
{
    var spots = await db.Spots.ToListAsync();
    var sectors = spots.GroupBy(s => s.SectorId).Select(g => new {
        sectorId = g.Key,
        spots = g.Select(s => new { s.SpotId, s.CurrentState, s.LastChangeTs })
    });
    return Results.Ok(new { sectors });
});

app.MapGet("/api/v1/sectors", async (ParkingContext db) =>
{
    var sectors = await db.Spots.GroupBy(s => s.SectorId)
        .Select(g => new {
            sectorId = g.Key,
            occupiedCount = g.Count(s => s.CurrentState == "OCCUPIED"),
            freeCount = g.Count(s => s.CurrentState == "FREE"),
            occupancyRate = 1.0 * g.Count(s => s.CurrentState == "OCCUPIED") / g.Count()
        }).ToListAsync();
    return Results.Ok(sectors);
});

app.MapGet("/api/v1/sectors/{sectorId}/spots", async (ParkingContext db, string sectorId) =>
{
    var spots = await db.Spots.Where(s => s.SectorId == sectorId).ToListAsync();
    return Results.Ok(spots);
});

app.MapGet("/api/v1/sectors/{sectorId}/free-spots", async (ParkingContext db, string sectorId, int? limit) =>
{
    var spots = await db.Spots.Where(s => s.SectorId == sectorId && s.CurrentState == "FREE").Take(limit ?? 10).ToListAsync();
    return Results.Ok(spots);
});

app.MapGet("/api/v1/reports/turnover", async (ParkingContext db, string? sectorId, DateTime? from, DateTime? to) =>
{
    // turnover = number of FREE->OCCUPIED transitions in period
    var q = db.SpotEvents.AsQueryable();
    if(sectorId!=null) q = q.Where(e => e.SectorId == sectorId);
    if(from!=null) q = q.Where(e => e.Ts >= from);
    if(to!=null) q = q.Where(e => e.Ts <= to);
    var count = await q.Where(e => e.State == "OCCUPIED").CountAsync();
    return Results.Ok(new { turnover = count });
});

app.MapGet("/api/v1/incidents", async (ParkingContext db, string? status) =>
{
    var q = db.Incidents.AsQueryable();
    if(status!=null) q = q.Where(i => i.Status == status);
    var list = await q.ToListAsync();
    return Results.Ok(list);
});

app.MapGet("/api/v1/recommendation", async (ParkingContext db, string fromSector) =>
{
    // If sector occupancy >= 0.9 recommend another sector with most free spots
    var stats = await db.Spots.GroupBy(s => s.SectorId).Select(g => new {
        sectorId = g.Key,
        freeCount = g.Count(s => s.CurrentState == "FREE"),
        total = g.Count()
    }).ToListAsync();
    var from = stats.FirstOrDefault(s => s.sectorId == fromSector);
    if(from == null) return Results.NotFound();
    var occRate = 1.0 - ((double)from.freeCount / from.total);
    if(occRate < 0.9) return Results.Ok(new { message = "fromSector not over threshold", occupancyRate = occRate });
    var candidate = stats.Where(s=>s.sectorId!=fromSector).OrderByDescending(s=>s.freeCount).FirstOrDefault();
    if(candidate==null) return Results.NotFound();
    var reason = $"Sector {fromSector} at {Math.Round(occRate*100)}% occupancy; Sector {candidate.sectorId} has {candidate.freeCount} free spots";
    var rec = new Backend.Models.RecommendationLog { Id = Guid.NewGuid(), Ts = DateTime.UtcNow, FromSector = fromSector, RecommendedSector = candidate.sectorId, Reason = reason, DataJson = System.Text.Json.JsonSerializer.Serialize(new { from = from, candidate = candidate }) };
    db.Recommendations.Add(rec);
    await db.SaveChangesAsync();
    return Results.Ok(new { fromSector, recommendedSector = candidate.sectorId, reason, ts = rec.Ts });
});

// ensure database exists and seed initial spots for sectors A,B,C
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ParkingContext>();
    // create database schema if missing
    db.Database.EnsureCreated();

    // seed spots if empty
    if (!db.Spots.Any())
    {
        var sectors = new[] { "A", "B", "C" };
        foreach (var s in sectors)
        {
            for (int i = 1; i <= 30; i++)
            {
                var id = $"{s}-{i.ToString().PadLeft(2, '0')}";
                db.Spots.Add(new Backend.Models.Spot { SpotId = id, SectorId = s, CurrentState = "FREE", LastChangeTs = DateTime.UtcNow, LastEventId = null });
            }
        }
        db.SaveChanges();
    }
}

app.Run();