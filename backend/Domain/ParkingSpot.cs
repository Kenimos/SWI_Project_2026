namespace ParkingReservation.Api.Domain;

/// <summary>
/// Resource rezervačního systému (viz docs/specification.md, sekce 0):
/// exkluzivní parkovací místo — v jednom okamžiku na něm smí mít nejvýše
/// jedna rezervace stav CONFIRMED (BR-02).
/// </summary>
public class ParkingSpot
{
    public int Id { get; set; }

    public int AreaId { get; set; }

    public ParkingArea? Area { get; set; }

    public ParkingSpotType Type { get; set; } = ParkingSpotType.Standard;

    /// <summary>
    /// Uložený provozní/fyzický stav místa (BR-06, BR-07): <c>Free</c>,
    /// <c>Occupied</c> (po check-inu) nebo <c>OutOfService</c>. <c>Reserved</c>
    /// se nikdy neukládá - je to odvozený pohled na interval, ve kterém místo
    /// blokuje CONFIRMED rezervace (viz CheckAreaAvailabilityAsync).
    /// </summary>
    public ParkingSpotState State { get; set; } = ParkingSpotState.Free;

    /// <summary>
    /// Precondition "ParkingSpot is active" z OP-02/OP-03/OP-05. Není to
    /// samostatný sloupec: místo je aktivní právě tehdy, když není
    /// OUT_OF_SERVICE (BR-06, jediný zdroj pravdy).
    /// </summary>
    public bool IsActive
    {
        get => State != ParkingSpotState.OutOfService;
        set
        {
            if (!value)
            {
                State = ParkingSpotState.OutOfService;
            }
            else if (State == ParkingSpotState.OutOfService)
            {
                State = ParkingSpotState.Free;
            }
        }
    }

    /// <summary>
    /// v0.2 (BR-05): pokud true, Confirm (OP-03) nevede přímo do CONFIRMED,
    /// ale do PENDING_APPROVAL - teprve Approve (OP-05) může potvrdit.
    /// </summary>
    public bool RequiresApproval { get; set; } = false;
}
