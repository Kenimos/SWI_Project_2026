using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Services;

/// <summary>
/// Implementuje OP-01 až OP-04 podle docs/specification.md.
/// </summary>
public class ReservationService
{
    private readonly ParkingDbContext _db;
    private readonly IClock _clock;

    public ReservationService(ParkingDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <summary>OP-01 — Create Reservation.</summary>
    public async Task<CreateReservationResult> CreateReservationAsync(CreateReservationRequest request)
    {
        // Precondition: User is authorized to create Reservations.
        // Zatím bez skutečného auth systému - jen validujeme, že UserId dává smysl.
        if (request.UserId <= 0)
        {
            return CreateReservationResult.Fail(CreateReservationError.Unauthorized);
        }

        // Precondition: start < end (BR-01).
        if (request.Start >= request.End)
        {
            return CreateReservationResult.Fail(CreateReservationError.InvalidInterval);
        }

        // Precondition: ParkingSpot exists.
        var spot = await _db.ParkingSpots.FindAsync(request.SpotId);
        if (spot is null)
        {
            return CreateReservationResult.Fail(CreateReservationError.SpotNotFound);
        }

        // Precondition: BR-04 eligibility for EvCharging / Disabled spots.
        if (spot.Type == ParkingSpotType.EvCharging && !request.VehicleIsElectric)
        {
            return CreateReservationResult.Fail(CreateReservationError.SpotRequiresEligibility);
        }

        if (spot.Type == ParkingSpotType.Disabled && !request.VehicleHasDisabledPermit)
        {
            return CreateReservationResult.Fail(CreateReservationError.SpotRequiresEligibility);
        }

        // Success postcondition: nová Reservation ve stavu DRAFT, spot zatím
        // není alokován (Create podle OP-01 nekontroluje kolize, viz BR-02).
        var reservation = new Reservation
        {
            SpotId = spot.Id,
            UserId = request.UserId,
            StartTime = request.Start,
            EndTime = request.End,
            State = ReservationState.Draft
        };

        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        return CreateReservationResult.Ok(reservation.Id, reservation.State);
    }

    /// <summary>OP-02 — Check Availability.</summary>
    public async Task<AvailabilityResult> CheckAvailabilityAsync(int spotId, DateTime start, DateTime end)
    {
        if (start >= end)
        {
            return AvailabilityResult.Fail(AvailabilityError.InvalidInterval);
        }

        var spotExists = await _db.ParkingSpots.AnyAsync(s => s.Id == spotId);
        if (!spotExists)
        {
            return AvailabilityResult.Fail(AvailabilityError.SpotNotFound);
        }

        // REQ-02: unavailable, pokud se interval překrývá s libovolnou CONFIRMED
        // rezervací téhož místa. Překryv dvou [start,end) intervalů (BR-01):
        // existing.Start < end && start < existing.End.
        var hasOverlap = await _db.Reservations.AnyAsync(r =>
            r.SpotId == spotId &&
            r.State == ReservationState.Confirmed &&
            r.StartTime < end &&
            start < r.EndTime);

        return AvailabilityResult.Ok(!hasOverlap);
    }

    /// <summary>
    /// OP-03 — Confirm Reservation. Souběžné pokusy o potvrzení rezervací
    /// téhož místa (REQ-04) i souběh Confirm/Cancel nad stejnou rezervací
    /// (BR-03) jsou serializované zámkem SpotLocks na dané ParkingSpot.
    /// </summary>
    public async Task<ConfirmReservationResult> ConfirmReservationAsync(int reservationId, int userId)
    {
        // Precondition: User is authorized to confirm Reservation X.
        // "Authorized" zatím jen jako u Create - bez auth systému (TBD v OP-03).
        if (userId <= 0)
        {
            return ConfirmReservationResult.Fail(ConfirmReservationError.Unauthorized);
        }

        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return ConfirmReservationResult.Fail(ConfirmReservationError.NotFound);
        }

        var spotLock = SpotLocks.Get(reservation.SpotId);
        await spotLock.WaitAsync();
        try
        {
            // Čerstvý stav z DB, ne to, co jsme viděli před získáním zámku.
            await _db.Entry(reservation).ReloadAsync();

            // Precondition: Reservation X.state = DRAFT.
            if (reservation.State != ReservationState.Draft)
            {
                return ConfirmReservationResult.Fail(ConfirmReservationError.InvalidState);
            }

            // Precondition: current time < Reservation X.start.
            if (_clock.UtcNow >= reservation.StartTime)
            {
                return ConfirmReservationResult.Fail(ConfirmReservationError.TooLate);
            }

            // Precondition: ParkingSpot is active.
            var spot = await _db.ParkingSpots.FindAsync(reservation.SpotId);
            if (spot is null || !spot.IsActive)
            {
                return ConfirmReservationResult.Fail(ConfirmReservationError.SpotInactive);
            }

            // REQ-03: interval nesmí kolidovat s jinou CONFIRMED rezervací
            // téhož místa (BR-01, BR-02).
            var overlaps = await _db.Reservations.AnyAsync(r =>
                r.Id != reservation.Id &&
                r.SpotId == reservation.SpotId &&
                r.State == ReservationState.Confirmed &&
                r.StartTime < reservation.EndTime &&
                reservation.StartTime < r.EndTime);

            if (overlaps)
            {
                return ConfirmReservationResult.Fail(ConfirmReservationError.Overlap);
            }

            reservation.State = ReservationState.Confirmed;
            await _db.SaveChangesAsync();

            return ConfirmReservationResult.Ok(reservation.State);
        }
        finally
        {
            spotLock.Release();
        }
    }

    /// <summary>OP-04 — Cancel Reservation.</summary>
    public async Task<CancelReservationResult> CancelReservationAsync(int reservationId, int userId)
    {
        // Precondition: User is authorized to cancel Reservation X (scope TBD v OP-04).
        if (userId <= 0)
        {
            return CancelReservationResult.Fail(CancelReservationError.Unauthorized);
        }

        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return CancelReservationResult.Fail(CancelReservationError.NotFound);
        }

        // Zrušení už zrušené rezervace je idempotentní úspěch (BR-03) - zámek netřeba.
        if (reservation.State == ReservationState.Cancelled)
        {
            return CancelReservationResult.Ok(reservation.State);
        }

        var spotLock = SpotLocks.Get(reservation.SpotId);
        await spotLock.WaitAsync();
        try
        {
            await _db.Entry(reservation).ReloadAsync();

            // Mohlo se mezitím (před ziskem zámku) stát CANCELLED souběžným Cancelem.
            if (reservation.State == ReservationState.Cancelled)
            {
                return CancelReservationResult.Ok(reservation.State);
            }

            // Precondition: current time < Reservation.start (BR-03).
            if (_clock.UtcNow >= reservation.StartTime)
            {
                return CancelReservationResult.Fail(CancelReservationError.TooLate);
            }

            // DRAFT nebo CONFIRMED -> CANCELLED (BR-03). Pokud mezitím vyhrál
            // Confirm, reservation.State je teď CONFIRMED - podle BR-03 je i
            // CONFIRMED->CANCELLED platný přechod, takže Cancel dál uspěje.
            reservation.State = ReservationState.Cancelled;
            await _db.SaveChangesAsync();

            return CancelReservationResult.Ok(reservation.State);
        }
        finally
        {
            spotLock.Release();
        }
    }
}
