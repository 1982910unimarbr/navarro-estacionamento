using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services
{
    public static class IncidentWriter
    {
        public static async Task AddIfNotExistsAsync(ParkingContext db, Incident incident, CancellationToken cancellationToken = default)
        {
            // Check if incident already exists with same SpotId, Type, Status
            var exists = await db.Incidents.AnyAsync(i =>
                i.SpotId == incident.SpotId &&
                i.Type == incident.Type &&
                i.Status == incident.Status,
                cancellationToken);

            if (!exists)
            {
                db.Incidents.Add(incident);
                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
