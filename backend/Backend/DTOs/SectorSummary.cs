namespace Backend.DTOs;

public class SectorSummary
{
    public required string SectorId { get; set; }
    public int OccupiedCount { get; set; }
    public int FreeCount { get; set; }
    public double OccupancyRate { get; set; }
    public DateTime? LastUpdateTs { get; set; }
}