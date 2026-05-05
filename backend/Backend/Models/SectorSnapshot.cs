using System;

namespace Backend.Models
{
    public class SectorSnapshot
    {
        public DateTime? Ts { get; set; }
        public string? SectorId { get; set; }
        public int OccupiedCount { get; set; }
        public int FreeCount { get; set; }
        public decimal OccupancyRate { get; set; }
    }
}
