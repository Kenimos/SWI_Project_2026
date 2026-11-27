namespace ParkingReservation.Api.Domain;

/// <summary>Parking spot type (see docs/intent-and-change.md).</summary>
public enum ParkingSpotType
{
    Standard,
    Compact,
    Disabled,
    EvCharging,
    Motorcycle
}
