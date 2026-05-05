using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Services;

var builder = WebApplication.CreateBuilder(args);

// Configuration
builder.Services.AddControllers();
// use minimal built-in OpenAPI helper
builder.Services.AddOpenApi();

var conn = builder.Configuration.GetConnectionString("Default") ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default") ?? "Host=postgres;Database=parking;Username=parking_user;Password=parking_pass";

builder.Services.AddDbContext<ParkingContext>(opt => opt.UseNpgsql(conn));

// MQTT subscriber background service
builder.Services.AddHostedService<MqttSubscriber>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

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
    var rec = new Backend.Models.RecommendationLog { Ts = DateTime.UtcNow, FromSector = fromSector, RecommendedSector = candidate.sectorId, Reason = reason };
    db.Recommendations.Add(rec);
    await db.SaveChangesAsync();
    return Results.Ok(new { fromSector, recommendedSector = candidate.sectorId, reason, ts = rec.Ts });
});

app.Run();