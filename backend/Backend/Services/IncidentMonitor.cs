using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backend.Services
{
    public class IncidentMonitor : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly int _stuckThresholdSec;
        private readonly int _scanIntervalSec;

        public IncidentMonitor(IServiceScopeFactory scopeFactory, IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _stuckThresholdSec = int.TryParse(Environment.GetEnvironmentVariable("STUCK_SECONDS"), out var st)
                ? st
                : configuration.GetValue<int?>("StuckSeconds") ?? 3600;
            _scanIntervalSec = int.TryParse(Environment.GetEnvironmentVariable("STUCK_SCAN_SECONDS"), out var scan)
                ? scan
                : configuration.GetValue<int?>("StuckScanSeconds") ?? 15;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var timer = new PeriodicTimer(TimeSpan.FromSeconds(_scanIntervalSec));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ParkingContext>();
                var now = DateTime.UtcNow;

                var spots = await db.Spots
                    .Where(s => s.LastChangeTs != null && s.CurrentState != null && s.LastEventId != null)
                    .ToListAsync(stoppingToken);

                foreach (var spot in spots)
                {
                    var age = now - spot.LastChangeTs!.Value.ToUniversalTime();
                    if (age.TotalSeconds < _stuckThresholdSec)
                    {
                        continue;
                    }

                    var type = spot.CurrentState == "OCCUPIED" ? "STUCK_OCCUPIED" : "STUCK_FREE";
                    var exists = await db.Incidents.AnyAsync(i =>
                        i.SpotId == spot.SpotId && i.Type == type && i.Status == "open", stoppingToken);

                    if (!exists)
                    {
                        var inc = new Backend.Models.Incident
                        {
                            Id = Guid.NewGuid(),
                            TsOpen = now,
                            Type = type,
                            Severity = 3,
                            SectorId = spot.SectorId,
                            SpotId = spot.SpotId,
                            EvidenceJson = JsonSerializer.Serialize(new { lastChange = spot.LastChangeTs, now }),
                            Status = "open"
                        };
                        await IncidentWriter.AddIfNotExistsAsync(db, inc, stoppingToken);
                    }
                }

                await db.SaveChangesAsync(stoppingToken);
            }
        }
    }
}
