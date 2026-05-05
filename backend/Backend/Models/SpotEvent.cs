using System;
using System.Text.Json;

namespace Backend.Models
{
    public class SpotEvent
    {
        public string? EventId { get; set; }
        public DateTime? Ts { get; set; }
        public string? SectorId { get; set; }
        public string? SpotId { get; set; }
        public string? State { get; set; }
        public string? RawPayloadJson { get; set; }
    }
}
