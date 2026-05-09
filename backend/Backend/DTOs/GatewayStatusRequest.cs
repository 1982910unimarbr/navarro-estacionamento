namespace Backend.DTOs;

public class GatewayStatusRequest
{
    public required string SectorId { get; set; }
    public string? Status { get; set; }
    public DateTime? Ts { get; set; }
}