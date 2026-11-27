namespace ParkingReservation.Api.Services;

/// <summary>
/// Source of "current time" (server UTC time, see section 0 of
/// docs/specification.md). An abstraction for deterministic tests.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
