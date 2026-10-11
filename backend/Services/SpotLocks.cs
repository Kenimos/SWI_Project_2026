using System.Collections.Concurrent;

namespace ParkingReservation.Api.Services;

/// <summary>
/// Jednoduchý per-proces zámek na jedno ParkingSpot id. Serializuje Confirm
/// (kontrola overlapu + zápis) a Cancel nad rezervacemi téhož místa, aby
/// souběžné požadavky nemohly obě "vyhrát" najednou (REQ-04, BR-03).
///
/// Architektonický limit (vědomě přijatý pro C02, viz future pressure Q v
/// docs/intent-and-change.md): funguje jen uvnitř jednoho procesu/instance.
/// Při více instancích aplikace nebo DB bez dalšího zámku toto nestačí —
/// to je architektonický driver pro C03, ne řešení pro teď.
/// </summary>
public static class SpotLocks
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Locks = new();

    public static SemaphoreSlim Get(int spotId) =>
        Locks.GetOrAdd(spotId, _ => new SemaphoreSlim(1, 1));
}
