namespace ParkingReservation.Api.Contracts;

/// <summary>Request pro OP-04 Cancel Reservation (docs/specification.md).</summary>
public record CancelReservationRequest(int UserId);
