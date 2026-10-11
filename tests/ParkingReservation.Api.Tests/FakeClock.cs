using ParkingReservation.Api.Services;

namespace ParkingReservation.Api.Tests;

/// <summary>Deterministické "current time" pro testy OP-03/OP-04.</summary>
public class FakeClock : IClock
{
    public DateTime UtcNow { get; set; }
}
