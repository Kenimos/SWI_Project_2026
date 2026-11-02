namespace ParkingReservation.Api.Domain;

public class ParkingArea
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public ParkingAreaType Type { get; set; } = ParkingAreaType.OpenLot;

    public string? OpeningHours { get; set; }

    public string? PricingPolicy { get; set; }

    public ICollection<ParkingSpot> Spots { get; set; } = new List<ParkingSpot>();
}
