namespace Backend.DTOs;

public class EventRequest
{
    public required string EventId { get; set; }
    public required string State { get; set; }
    public required string SectorId { get; set; }
    public required string SpotId { get; set; }
    public required DateTime Ts { get; set; }
}