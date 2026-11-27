using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Contracts;

/// <summary>Reason for rejecting OP-01 Create Reservation, see "Alternative / failure outcomes" in docs/specification.md.</summary>
public enum CreateReservationError
{
    Unauthorized,
    SpotNotFound,
    InvalidInterval,
    SpotRequiresEligibility
}

public record CreateReservationResult
{
    public bool Success { get; init; }
    public int? ReservationId { get; init; }
    public ReservationState? State { get; init; }
    public CreateReservationError? Error { get; init; }

    public static CreateReservationResult Ok(int id, ReservationState state) =>
        new() { Success = true, ReservationId = id, State = state };

    public static CreateReservationResult Fail(CreateReservationError error) =>
        new() { Success = false, Error = error };
}

/// <summary>Reason for rejecting OP-02 Check Availability.</summary>
public enum AvailabilityError
{
    SpotNotFound,
    InvalidInterval
}

public record AvailabilityResult
{
    public bool Success { get; init; }
    public bool Available { get; init; }
    public AvailabilityError? Error { get; init; }

    public static AvailabilityResult Ok(bool available) => new() { Success = true, Available = available };

    public static AvailabilityResult Fail(AvailabilityError error) => new() { Success = false, Error = error };
}

/// <summary>Reason for rejecting OP-03 Confirm Reservation, see "Failure outcomes" in docs/specification.md.</summary>
public enum ConfirmReservationError
{
    NotFound,
    Unauthorized,
    InvalidState,
    TooLate,
    SpotInactive,
    Overlap
}

public record ConfirmReservationResult
{
    public bool Success { get; init; }
    public ReservationState? State { get; init; }
    public ConfirmReservationError? Error { get; init; }

    public static ConfirmReservationResult Ok(ReservationState state) => new() { Success = true, State = state };

    public static ConfirmReservationResult Fail(ConfirmReservationError error) => new() { Success = false, Error = error };
}

/// <summary>Reason for rejecting OP-04 Cancel Reservation, see "Failure outcomes" in docs/specification.md.</summary>
public enum CancelReservationError
{
    NotFound,
    Unauthorized,
    TooLate,

    /// <summary>Confirm persisted first while this request was cancelling a DRAFT (BR-03).</summary>
    Conflict,

    /// <summary>v0.2: REJECTED/EXPIRED/COMPLETED are terminal - nothing left to cancel (BR-03 v0.2).</summary>
    InvalidState
}

public record CancelReservationResult
{
    public bool Success { get; init; }
    public ReservationState? State { get; init; }
    public CancelReservationError? Error { get; init; }

    public static CancelReservationResult Ok(ReservationState state) => new() { Success = true, State = state };

    public static CancelReservationResult Fail(CancelReservationError error) => new() { Success = false, Error = error };
}

/// <summary>v0.2 — Reason for rejecting OP-05 Approve (approve branch), see docs/specification.md.</summary>
public enum ApproveReservationError
{
    NotFound,
    Unauthorized,
    InvalidState,
    Expired,
    TooLate,
    SpotInactive,
    Overlap
}

public record ApproveReservationResult
{
    public bool Success { get; init; }
    public ReservationState? State { get; init; }
    public ApproveReservationError? Error { get; init; }

    public static ApproveReservationResult Ok(ReservationState state) => new() { Success = true, State = state };

    public static ApproveReservationResult Fail(ApproveReservationError error) => new() { Success = false, Error = error };
}

/// <summary>v0.2 — Reason for rejecting OP-05 Reject (reject branch), see docs/specification.md.</summary>
public enum RejectReservationError
{
    NotFound,
    Unauthorized,
    InvalidState,
    Expired
}

public record RejectReservationResult
{
    public bool Success { get; init; }
    public ReservationState? State { get; init; }
    public RejectReservationError? Error { get; init; }

    public static RejectReservationResult Ok(ReservationState state) => new() { Success = true, State = state };

    public static RejectReservationResult Fail(RejectReservationError error) => new() { Success = false, Error = error };
}
