using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Contracts;

/// <summary>Důvod odmítnutí OP-01 Create Reservation, viz "Alternative / failure outcomes" v docs/specification.md.</summary>
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

/// <summary>Důvod odmítnutí OP-02 Check Availability.</summary>
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

/// <summary>Důvod odmítnutí OP-03 Confirm Reservation, viz "Failure outcomes" v docs/specification.md.</summary>
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

/// <summary>Důvod odmítnutí OP-04 Cancel Reservation, viz "Failure outcomes" v docs/specification.md.</summary>
public enum CancelReservationError
{
    NotFound,
    Unauthorized,
    TooLate
}

public record CancelReservationResult
{
    public bool Success { get; init; }
    public ReservationState? State { get; init; }
    public CancelReservationError? Error { get; init; }

    public static CancelReservationResult Ok(ReservationState state) => new() { Success = true, State = state };

    public static CancelReservationResult Fail(CancelReservationError error) => new() { Success = false, Error = error };
}
