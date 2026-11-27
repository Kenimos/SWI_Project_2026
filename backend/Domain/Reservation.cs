namespace ParkingReservation.Api.Domain;

/// <summary>Rezervace jednoho parkovacího místa na časové okno.</summary>
public class Reservation
{
    public int Id { get; set; }

    /// <summary>Rezervované parkovací místo.</summary>
    public int SpotId { get; set; }

    public ParkingSpot? Spot { get; set; }

    /// <summary>Uživatel (role Driver), který rezervaci vytvořil a jediný ji smí potvrdit/zrušit.</summary>
    public int UserId { get; set; }

    public User? User { get; set; }

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

    /// <summary>
    /// OP-07: okamžik check-inu (UTC). Null, dokud vozidlo nepřijelo. Váže
    /// fyzické obsazení místa ke konkrétní rezervaci (BR-07).
    /// </summary>
    public DateTime? CheckedInAt { get; set; }
}
