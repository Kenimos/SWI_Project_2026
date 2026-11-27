using ParkingReservation.Api.Services;

namespace ParkingReservation.Api.Tests;

/// <summary>Deterministic "current time" for the tests.</summary>
public class FakeClock : IClock
{
    public DateTime UtcNow { get; set; }
}
