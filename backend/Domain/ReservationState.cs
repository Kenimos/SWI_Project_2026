namespace ParkingReservation.Api.Domain;

/// <summary>Reservation state (see docs/specification.md).</summary>
public enum ReservationState
{
    Draft,
    Confirmed,
    Cancelled,

    /// <summary>v0.2 - waiting for the Approver's decision (OP-05), see BR-05.</summary>
    PendingApproval,

    /// <summary>v0.2 - the Approver rejected the request (terminal state).</summary>
    Rejected,

    /// <summary>v0.2 - the approval window elapsed without a decision (terminal state).</summary>
    Expired,

    /// <summary>v0.2.1 - after check-out (OP-07); the reservation no longer blocks the spot (terminal state).</summary>
    Completed
}
