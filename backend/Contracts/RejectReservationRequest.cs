namespace ParkingReservation.Api.Contracts;

/// <summary>Request pro OP-05 Approve Reservation - zamítnutí (docs/specification.md).</summary>
public record RejectReservationRequest(int ApproverUserId);
