using System;

namespace Backend.Models
{
    public class RecommendationLog
    {
        public Guid Id { get; set; }
        public DateTime? Ts { get; set; }
        public string? FromSector { get; set; }
        public string? RecommendedSector { get; set; }
        public string? Reason { get; set; }
        public string? DataJson { get; set; }
    }
}
