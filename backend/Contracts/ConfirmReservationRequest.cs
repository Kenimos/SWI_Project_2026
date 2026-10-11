namespace ParkingReservation.Api.Contracts;

/// <summary>Request pro OP-03 Confirm Reservation (docs/specification.md).</summary>
public record ConfirmReservationRequest(int UserId);
