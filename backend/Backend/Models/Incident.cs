using System;

namespace Backend.Models
{
    public class Incident
    {
        public Guid Id { get; set; }
        public DateTime? TsOpen { get; set; }
        public DateTime? TsClose { get; set; }
        public string? Type { get; set; }
        public int Severity { get; set; }
        public string? SectorId { get; set; }
        public string? SpotId { get; set; }
        public string? EvidenceJson { get; set; }
        public string? Status { get; set; }
    }
}
