using Microsoft.AspNetCore.Mvc;
using Backend.Data;
using Backend.DTOs;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

[ApiController]
[Route("api/v1")]
public class PublicController : ControllerBase
{
    private readonly ParkingContext _db;

    public PublicController(ParkingContext db)
    {
        _db = db;
    }

    [HttpGet("map")]
    [ProducesResponseType(typeof(MapResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMap()
    {
        var spots = await _db.Spots.ToListAsync();
        var sectors = spots.GroupBy(s => s.SectorId).Select(g => new SectorInfo
        {
            SectorId = g.Key,
            Spots = g.Select(s => new SpotInfo { SpotId = s.SpotId, CurrentState = s.CurrentState, LastChangeTs = s.LastChangeTs }).ToList()
        }).ToList();
        return Ok(new MapResponse { Sectors = sectors });
    }

    [HttpGet("sectors")]
    [ProducesResponseType(typeof(List<SectorSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSectors()
    {
        var sectors = await _db.Spots.GroupBy(s => s.SectorId)
            .Select(g => new SectorSummary
            {
                SectorId = g.Key,
                OccupiedCount = g.Count(s => s.CurrentState == "OCCUPIED"),
                FreeCount = g.Count(s => s.CurrentState == "FREE"),
                OccupancyRate = g.Count() > 0 ? 1.0 * g.Count(s => s.CurrentState == "OCCUPIED") / g.Count() : 0,
                LastUpdateTs = g.Max(s => s.LastChangeTs)
            }).ToListAsync();
        return Ok(sectors);
    }

    [HttpGet("sectors/{sectorId}/spots")]
    [ProducesResponseType(typeof(List<Models.Spot>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSpots(string sectorId)
    {
        var spots = await _db.Spots.Where(s => s.SectorId == sectorId).ToListAsync();
        return Ok(spots);
    }

    [HttpGet("sectors/{sectorId}/free-spots")]
    [ProducesResponseType(typeof(List<Models.Spot>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetFreeSpots(string sectorId, [FromQuery] int? limit = 10)
    {
        var spots = await _db.Spots.Where(s => s.SectorId == sectorId && s.CurrentState == "FREE").Take(limit.Value).ToListAsync();
        return Ok(spots);
    }

    [HttpGet("reports/turnover")]
    [ProducesResponseType(typeof(TurnoverResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTurnover([FromQuery] string? sectorId, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
    {
        var q = _db.SpotEvents.AsQueryable();
        if (sectorId != null) q = q.Where(e => e.SectorId == sectorId);
        if (from != null) q = q.Where(e => e.Ts >= from);
        if (to != null) q = q.Where(e => e.Ts <= to);
        var count = await q.Where(e => e.State == "OCCUPIED").CountAsync();
        return Ok(new TurnoverResponse { Turnover = count });
    }

    [HttpGet("incidents")]
    [ProducesResponseType(typeof(List<Models.Incident>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetIncidents([FromQuery] string? status)
    {
        var q = _db.Incidents.AsQueryable();
        if (status != null) q = q.Where(i => i.Status == status);
        var list = await q.ToListAsync();
        return Ok(list);
    }

    [HttpGet("recommendation")]
    [ProducesResponseType(typeof(RecommendationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecommendation([FromQuery] string fromSector)
    {
        var stats = await _db.Spots.GroupBy(s => s.SectorId).Select(g => new
        {
            sectorId = g.Key,
            freeCount = g.Count(s => s.CurrentState == "FREE"),
            total = g.Count()
        }).ToListAsync();
        var from = stats.FirstOrDefault(s => s.sectorId == fromSector);
        if (from == null) return NotFound();
        var occRate = 1.0 - ((double)from.freeCount / from.total);
        if (occRate < 0.9) return Ok(new RecommendationResponse { Message = "fromSector not over threshold", OccupancyRate = occRate });
        var candidate = stats.Where(s => s.sectorId != fromSector).OrderByDescending(s => s.freeCount).FirstOrDefault();
        if (candidate == null) return NotFound();
        var reason = $"Sector {fromSector} at {Math.Round(occRate * 100)}% occupancy; Sector {candidate.sectorId} has {candidate.freeCount} free spots";
        var rec = new Models.RecommendationLog { Id = Guid.NewGuid(), Ts = DateTime.UtcNow, FromSector = fromSector, RecommendedSector = candidate.sectorId, Reason = reason, DataJson = System.Text.Json.JsonSerializer.Serialize(new { from = from, candidate = candidate }) };
        _db.Recommendations.Add(rec);
        await _db.SaveChangesAsync();

        return Ok(new RecommendationResponse { FromSector = fromSector, RecommendedSector = candidate.sectorId, Reason = reason, Ts = rec.Ts });
    }
}