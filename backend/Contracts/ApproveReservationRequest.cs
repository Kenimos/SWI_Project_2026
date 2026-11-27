namespace ParkingReservation.Api.Contracts;

/// <summary>Request for OP-05 Approve Reservation - approval (docs/specification.md).</summary>
public record ApproveReservationRequest(int ApproverUserId);
