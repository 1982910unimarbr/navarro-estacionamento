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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Spot>().HasKey(s => s.SpotId);
            modelBuilder.Entity<SpotEvent>().HasKey(e => e.EventId);
            modelBuilder.Entity<SectorSnapshot>().HasKey(s => new { s.Ts, s.SectorId });
            modelBuilder.Entity<RecommendationLog>().HasKey(r => r.Id);
            // prevent duplicate open incidents for same spot/type
            modelBuilder.Entity<Incident>().HasIndex(i => new { i.SpotId, i.Type, i.Status }).IsUnique(true);
        }
    }
}
