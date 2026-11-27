namespace ParkingReservation.Api.Domain;

/// <summary>Reservation of one parking spot for a time window.</summary>
public class Reservation
{
    public int Id { get; set; }

    /// <summary>The reserved parking spot.</summary>
    public int SpotId { get; set; }

    public ParkingSpot? Spot { get; set; }

    /// <summary>The user (role Driver) who created the reservation and the only one who may confirm/cancel it.</summary>
    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Start of the reservation (UTC).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>End of the reservation (UTC), exclusive (BR-01).</summary>
    public DateTime EndTime { get; set; }

    public ReservationState State { get; set; } = ReservationState.Draft;

    /// <summary>
    /// v0.2: the moment the reservation entered PENDING_APPROVAL. Null outside
    /// that state. Used for lazy detection of the approval window expiry
    /// (REQ-08) - see docs/specification.md, OP-05.
    /// </summary>
    public DateTime? PendingApprovalSince { get; set; }

    /// <summary>
    /// OP-07: the moment of check-in (UTC). Null until the vehicle arrives. Binds
    /// the physical occupancy of the spot to a specific reservation (BR-07).
    /// </summary>
    public DateTime? CheckedInAt { get; set; }
}
