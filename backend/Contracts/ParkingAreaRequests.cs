using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Contracts;

public record CreateParkingAreaRequest(
    int ApproverUserId,
    string Name,
    string? Address,
    ParkingAreaType Type,
    string? OpeningHours,
    string? PricingPolicy);

public record CreateParkingSpotRequest(
    int ApproverUserId,
    ParkingSpotType Type);

public record SpotOperationRequest(int ApproverUserId);
