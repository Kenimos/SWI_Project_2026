namespace ParkingReservation.Api.Contracts;

/// <summary>Request for OP-04 Cancel Reservation (docs/specification.md).</summary>
public record CancelReservationRequest(int UserId);
