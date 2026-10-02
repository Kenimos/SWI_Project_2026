namespace ParkingReservation.Api.Contracts;

/// <summary>Request pro OP-01 Create Reservation (docs/specification.md).</summary>
/// <param name="UserId">Id přihlášeného uživatele. 0/záporné = neautorizováno (zatím bez skutečného auth systému).</param>
/// <param name="SpotId">Id parkovacího místa, které má být rezervováno.</param>
/// <param name="Start">Začátek intervalu (UTC).</param>
/// <param name="End">Konec intervalu (UTC), exkluzivní (BR-01).</param>
/// <param name="VehicleIsElectric">Vyžadováno pro BR-04 u míst typu EvCharging.</param>
/// <param name="VehicleHasDisabledPermit">Vyžadováno pro BR-04 u míst typu Disabled.</param>
public record CreateReservationRequest(
    int UserId,
    int SpotId,
    DateTime Start,
    DateTime End,
    bool VehicleIsElectric = false,
    bool VehicleHasDisabledPermit = false);
