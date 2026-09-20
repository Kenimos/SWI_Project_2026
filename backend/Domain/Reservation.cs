namespace ParkingReservation.Api.Domain;

/// <summary>Rezervace jednoho parkovacího místa na časové okno.</summary>
public class Reservation
{
    public int Id { get; set; }

    /// <summary>Rezervované parkovací místo (zatím jen číselná reference, entita ParkingSpot ještě neexistuje).</summary>
    public int SpotId { get; set; }

    /// <summary>Uživatel, který rezervaci vytvořil (zatím jen číselná reference).</summary>
    public int UserId { get; set; }

    /// <summary>Začátek rezervace (UTC).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Konec rezervace (UTC).</summary>
    public DateTime EndTime { get; set; }

    public ReservationState State { get; set; } = ReservationState.Draft;
}
