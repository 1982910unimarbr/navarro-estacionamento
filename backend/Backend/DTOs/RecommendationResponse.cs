namespace Backend.DTOs;

public class RecommendationResponse
{
    public string? FromSector { get; set; }
    public string? RecommendedSector { get; set; }
    public string? Reason { get; set; }
    public DateTime? Ts { get; set; }
    public string? Message { get; set; }
    public double? OccupancyRate { get; set; }
}