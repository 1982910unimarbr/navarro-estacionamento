namespace Backend.DTOs;

public class MapResponse
{
    public required List<SectorInfo> Sectors { get; set; }
}

public class SectorInfo
{
    public required string SectorId { get; set; }
    public required List<SpotInfo> Spots { get; set; }
}

public class SpotInfo
{
    public required string SpotId { get; set; }
    public required string CurrentState { get; set; }
    public DateTime? LastChangeTs { get; set; }
}