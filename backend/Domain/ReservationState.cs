namespace ParkingReservation.Api.Domain;

/// <summary>Stav rezervace (viz docs/specification.md).</summary>
public enum ReservationState
{
    Draft,
    Confirmed,
    Cancelled,

    /// <summary>v0.2 - čeká na rozhodnutí Approvera (OP-05), viz BR-05.</summary>
    PendingApproval,

    /// <summary>v0.2 - Approver žádost zamítl (terminální stav).</summary>
    Rejected,

    /// <summary>v0.2 - vypršelo schvalovací okno bez rozhodnutí (terminální stav).</summary>
    Expired
}
