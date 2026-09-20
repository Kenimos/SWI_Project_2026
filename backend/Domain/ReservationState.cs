namespace ParkingReservation.Api.Domain;

/// <summary>Stav rezervace (viz docs/intent-and-change.md).</summary>
public enum ReservationState
{
    Draft,
    Confirmed,
    Cancelled
}
