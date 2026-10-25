namespace ParkingReservation.Api.Contracts;

public record CheckInReservationRequest(int ApproverUserId);

public record CheckOutReservationRequest(int ApproverUserId);
