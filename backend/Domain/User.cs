namespace ParkingReservation.Api.Domain;

public class User
{
    public int Id { get; set; }

    public UserRole Role { get; set; } = UserRole.Driver;

    public bool IsActive { get; set; } = true;
}
