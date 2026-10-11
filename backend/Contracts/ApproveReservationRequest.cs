namespace ParkingReservation.Api.Contracts;

/// <summary>Request pro OP-05 Approve Reservation - schválení (docs/specification.md).</summary>
public record ApproveReservationRequest(int ApproverUserId);
