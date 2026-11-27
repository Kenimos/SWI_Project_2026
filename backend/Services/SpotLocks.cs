using System.Collections.Concurrent;

namespace ParkingReservation.Api.Services;

/// <summary>
/// A simple per-process lock for one ParkingSpot id. It serializes Confirm
/// (overlap check + write), Cancel, Approve/Reject, check-in/out and spot
/// operability changes on the same spot, so that concurrent requests cannot
/// both "win" at once (REQ-04, BR-03).
///
/// Architectural limit (consciously accepted, see future pressure Q in
/// docs/intent-and-change.md and ADR-006): it works only inside one process/
/// instance. With multiple application instances this is not enough.
/// </summary>
public static class SpotLocks
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    public static SemaphoreSlim Get(int spotId) =>
        Locks.GetOrAdd(spotId, _ => new SemaphoreSlim(1, 1));
}
