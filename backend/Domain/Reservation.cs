namespace ParkingReservation.Api.Domain;

/// <summary>Rezervace jednoho parkovacího místa na časové okno.</summary>
public class Reservation
{
    public int Id { get; set; }

    /// <summary>Rezervované parkovací místo.</summary>
    public int SpotId { get; set; }

    public ParkingSpot? Spot { get; set; }

    /// <summary>Uživatel, který rezervaci vytvořil (zatím jen číselná reference, entita User ještě neexistuje).</summary>
    public int UserId { get; set; }

    /// <summary>Začátek rezervace (UTC).</summary>
    public DateTime StartTime { get; set; }

    /// <summary>Konec rezervace (UTC).</summary>
    public DateTime EndTime { get; set; }

    public ReservationState State { get; set; } = ReservationState.Draft;

    /// <summary>
    /// v0.2: okamžik, kdy rezervace přešla do PENDING_APPROVAL. Null mimo
    /// tento stav. Slouží k líné detekci vypršení schvalovacího okna
    /// (REQ-08) - viz docs/specification.md, OP-05.
    /// </summary>
    public DateTime? PendingApprovalSince { get; set; }
}
