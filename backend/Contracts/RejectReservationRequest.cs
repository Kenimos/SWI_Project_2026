namespace ParkingReservation.Api.Contracts;

/// <summary>Request for OP-05 Approve Reservation - rejection (docs/specification.md).</summary>
public record RejectReservationRequest(int ApproverUserId);
