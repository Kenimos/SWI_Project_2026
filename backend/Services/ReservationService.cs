using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Services;

/// <summary>
/// Implementuje OP-01 Create Reservation a OP-02 Check Availability
/// podle docs/specification.md. OP-03 (Confirm) a OP-04 (Cancel) zatím
/// nejsou implementované — čekají na dopsanou specifikaci.
/// </summary>
public class ReservationService
{
    private readonly ParkingDbContext _db;

    public ReservationService(ParkingDbContext db)
    {
        _db = db;
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
}
