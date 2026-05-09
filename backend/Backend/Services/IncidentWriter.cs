using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services
{
    public static class IncidentWriter
    {
        public static Task AddIfNotExistsAsync(ParkingContext db, Incident incident, CancellationToken cancellationToken = default)
        {
            return db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO ""Incidents"" (""Id"", ""TsOpen"", ""TsClose"", ""Type"", ""Severity"", ""SectorId"", ""SpotId"", ""EvidenceJson"", ""Status"")
                VALUES ({incident.Id}, {incident.TsOpen}, {incident.TsClose}, {incident.Type}, {incident.Severity}, {incident.SectorId}, {incident.SpotId}, {incident.EvidenceJson}, {incident.Status})
                ON CONFLICT (""SpotId"", ""Type"", ""Status"") DO NOTHING;
            ", cancellationToken);
        }
    }
}
