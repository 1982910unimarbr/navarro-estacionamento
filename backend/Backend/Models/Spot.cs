using System;

namespace Backend.Models
{
    public class Spot
    {
        public string? SpotId { get; set; }
        public string? SectorId { get; set; }
        public string? CurrentState { get; set; }
        public DateTime? LastChangeTs { get; set; }
        public string? LastEventId { get; set; }
    }
}
