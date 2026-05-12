namespace Backend.DTOs;

public class IncidentRequest
{
    public required string EventId { get; set; }
    public required string SectorId { get; set; }
    public required string SpotId { get; set; }
    public required string Type { get; set; }
    public int? Severity { get; set; }
    public required DateTime Ts { get; set; }
    public string? Status { get; set; }
    public string? Source { get; set; }
}
