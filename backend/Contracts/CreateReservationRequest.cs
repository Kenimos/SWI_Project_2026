namespace ParkingReservation.Api.Contracts;

/// <summary>Request for OP-01 Create Reservation (docs/specification.md).</summary>
/// <param name="UserId">Id of the acting user; must be an active Driver (spec section 0, local identity without a real auth system).</param>
/// <param name="SpotId">Id of the parking spot to reserve.</param>
/// <param name="Start">Start of the interval (UTC).</param>
/// <param name="End">End of the interval (UTC), exclusive (BR-01).</param>
/// <param name="VehicleIsElectric">Required by BR-04 for EvCharging spots.</param>
/// <param name="VehicleHasDisabledPermit">Required by BR-04 for Disabled spots.</param>
public record CreateReservationRequest(
    int UserId,
    int SpotId,
    DateTime Start,
    DateTime End,
    bool VehicleIsElectric = false,
    bool VehicleHasDisabledPermit = false);
