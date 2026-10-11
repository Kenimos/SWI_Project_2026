namespace ParkingReservation.Api.Services;

/// <summary>
/// Zdroj "current time" pro OP-03/OP-04 (server UTC time, viz Assumption
/// v docs/specification.md). Abstrakce kvůli deterministickým testům.
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
