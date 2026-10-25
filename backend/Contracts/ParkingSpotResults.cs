using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Contracts;

public record ParkingSpotResult
{
    public bool Success { get; init; }
    public int? SpotId { get; init; }
    public ParkingAreaError? Error { get; init; }

    public static ParkingSpotResult Ok(int spotId) => new() { Success = true, SpotId = spotId };

    public static ParkingSpotResult Fail(ParkingAreaError error) => new() { Success = false, Error = error };
}
