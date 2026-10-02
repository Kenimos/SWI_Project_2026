namespace ParkingReservation.Api.Domain;

/// <summary>
/// Resource rezervačního systému (viz docs/specification.md, sekce 0):
/// exkluzivní parkovací místo — v jednom okamžiku na něm smí mít nejvýše
/// jedna rezervace stav CONFIRMED (BR-02).
/// </summary>
public class ParkingSpot
{
    public int Id { get; set; }

    public ParkingSpotType Type { get; set; } = ParkingSpotType.Standard;

    /// <summary>Odpovídá precondition "ParkingSpot is active" v OP-03 (Confirm).</summary>
    public bool IsActive { get; set; } = true;
}
