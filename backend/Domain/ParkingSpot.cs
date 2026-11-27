namespace ParkingReservation.Api.Domain;

/// <summary>
/// Resource of the reservation system (see docs/specification.md, section 0):
/// an exclusive parking spot — at any moment at most one reservation on it
/// may be CONFIRMED (BR-02).
/// </summary>
public class ParkingSpot
{
    public int Id { get; set; }

    public int AreaId { get; set; }

    public ParkingArea? Area { get; set; }

    public ParkingSpotType Type { get; set; } = ParkingSpotType.Standard;

    /// <summary>
    /// Stored operational/physical state of the spot (BR-06, BR-07): <c>Free</c>,
    /// <c>Occupied</c> (after check-in) or <c>OutOfService</c>. <c>Reserved</c>
    /// is never stored - it is a derived view of an interval in which the spot
    /// is blocked by a CONFIRMED reservation (see CheckAreaAvailabilityAsync).
    /// </summary>
    public ParkingSpotState State { get; set; } = ParkingSpotState.Free;

    /// <summary>
    /// Precondition "ParkingSpot is active" of OP-02/OP-03/OP-05. It is not a
    /// separate column: the spot is active exactly when it is not
    /// OUT_OF_SERVICE (BR-06, single source of truth).
    /// </summary>
    public bool IsActive
    {
        get => State != ParkingSpotState.OutOfService;
        set
        {
            if (!value)
            {
                State = ParkingSpotState.OutOfService;
            }
            else if (State == ParkingSpotState.OutOfService)
            {
                State = ParkingSpotState.Free;
            }
        }
    }

    /// <summary>
    /// v0.2 (BR-05): if true, Confirm (OP-03) does not lead directly to CONFIRMED
    /// but to PENDING_APPROVAL - only Approve (OP-05) can confirm.
    /// </summary>
    public bool RequiresApproval { get; set; } = false;
}
