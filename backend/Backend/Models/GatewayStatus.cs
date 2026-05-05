using System;

namespace Backend.Models
{
    public class GatewayStatus
    {
        public string SectorId { get; set; }
        public string? Status { get; set; }
        public DateTime? LastSeen { get; set; }
    }
}
