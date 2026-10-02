namespace ParkingReservation.Api.Domain;

/// <summary>Typ parkovacího místa (viz docs/intent-and-change.md).</summary>
public enum ParkingSpotType
{
    Standard,
    Compact,
    Disabled,
    EvCharging,
    Motorcycle
}
