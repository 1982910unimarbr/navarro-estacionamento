using Microsoft.EntityFrameworkCore;
using Backend.Models;

namespace Backend.Data
{
    public class ParkingContext : DbContext
    {
        public ParkingContext(DbContextOptions<ParkingContext> opts) : base(opts) { }

        public DbSet<Spot> Spots { get; set; }
        public DbSet<SpotEvent> SpotEvents { get; set; }
        public DbSet<SectorSnapshot> SectorSnapshots { get; set; }
        public DbSet<Incident> Incidents { get; set; }
        public DbSet<RecommendationLog> Recommendations { get; set; }
        public DbSet<GatewayStatus> GatewayStatuses { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Spot>().HasKey(s => s.SpotId);
            modelBuilder.Entity<SpotEvent>().HasKey(e => e.EventId);
            modelBuilder.Entity<SectorSnapshot>().HasKey(s => new { s.Ts, s.SectorId });
            modelBuilder.Entity<RecommendationLog>().HasKey(r => r.Id);
            // Note: Unique constraint removed - handled via ON CONFLICT DO NOTHING in SQL
            modelBuilder.Entity<GatewayStatus>().HasKey(g => g.SectorId);
        }
    }
}
