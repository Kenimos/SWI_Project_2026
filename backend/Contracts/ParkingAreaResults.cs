using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Contracts;

public enum ParkingAreaError
{
    Unauthorized,
    NotFound,
    InvalidName
}

public record ParkingAreaResult
{
    public bool Success { get; init; }
    public int? AreaId { get; init; }
    public ParkingAreaError? Error { get; init; }

    public static ParkingAreaResult Ok(int areaId) => new() { Success = true, AreaId = areaId };

    public static ParkingAreaResult Fail(ParkingAreaError error) => new() { Success = false, Error = error };
}

public enum ParkingSpotOperationError
{
    Unauthorized,
    NotFound,
    InvalidState
}

public record ParkingSpotOperationResult
{
    public bool Success { get; init; }
    public ParkingSpotState? State { get; init; }
    public ParkingSpotOperationError? Error { get; init; }

    public static ParkingSpotOperationResult Ok(ParkingSpotState state) => new() { Success = true, State = state };

    public static ParkingSpotOperationResult Fail(ParkingSpotOperationError error) => new() { Success = false, Error = error };
}

public record ParkingAreaAvailabilityItem(int SpotId, ParkingSpotType Type, ParkingSpotState State, bool Available);

public enum ParkingAreaAvailabilityError
{
    NotFound,
    InvalidInterval
}

public record ParkingAreaAvailabilityResult
{
    public bool Success { get; init; }
    public IReadOnlyList<ParkingAreaAvailabilityItem> Spots { get; init; } = [];
    public ParkingAreaAvailabilityError? Error { get; init; }

    public static ParkingAreaAvailabilityResult Ok(IReadOnlyList<ParkingAreaAvailabilityItem> spots) => new() { Success = true, Spots = spots };

    public static ParkingAreaAvailabilityResult Fail(ParkingAreaAvailabilityError error) => new() { Success = false, Error = error };
}
