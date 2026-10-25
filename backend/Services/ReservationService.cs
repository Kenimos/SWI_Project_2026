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
    private readonly TimeSpan _approvalWindow;

    /// <param name="approvalWindow">
    /// v0.2 - jak dlouho smí PENDING_APPROVAL čekat, než se při dalším
    /// dotyku (Approve/Reject/Cancel) považuje za EXPIRED (REQ-08).
    /// Přesná hodnota je v docs/specification.md výslovně TBD - tohle je
    /// jen demonstrační placeholder, ne odsouhlasené business pravidlo.
    /// </param>
    public ReservationService(ParkingDbContext db, IClock clock, TimeSpan approvalWindow)
    {
        _db = db;
        _clock = clock;
        _approvalWindow = approvalWindow;
    }

    /// <summary>OP-01 — Create Reservation.</summary>
    public async Task<CreateReservationResult> CreateReservationAsync(CreateReservationRequest request)
    {
        var user = await _db.Users.FindAsync(request.UserId);
        if (user is null || !user.IsActive || user.Role != UserRole.Driver)
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

        var spot = await _db.ParkingSpots.FindAsync(spotId);
        if (spot is null)
        {
            return AvailabilityResult.Fail(AvailabilityError.SpotNotFound);
        }

        // REQ-02: neaktivní místo je vždy UNAVAILABLE, bez ohledu na rezervace
        // (stejný invariant jako v OP-03 - viz konzistenční kontrola v sekci 4).
        if (!spot.IsActive)
        {
            return AvailabilityResult.Ok(available: false);
        }

        // Jinak unavailable, pokud se interval překrývá s libovolnou CONFIRMED
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
        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return ConfirmReservationResult.Fail(ConfirmReservationError.NotFound);
        }

        if (!await IsReservationOwnerDriverAsync(reservation, userId))
        {
            return ConfirmReservationResult.Fail(ConfirmReservationError.Unauthorized);
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

            // BR-05 (v0.2): spoty vyžadující schválení jdou do PENDING_APPROVAL
            // místo přímo do CONFIRMED - kontrola overlapu se odkládá na Approve.
            if (spot.RequiresApproval)
            {
                reservation.State = ReservationState.PendingApproval;
                reservation.PendingApprovalSince = _clock.UtcNow;
                spot.State = ParkingSpotState.Reserved;
                await _db.SaveChangesAsync();

                return ConfirmReservationResult.Ok(reservation.State);
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
            spot.State = ParkingSpotState.Reserved;
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
        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return CancelReservationResult.Fail(CancelReservationError.NotFound);
        }

        if (!await IsReservationOwnerDriverAsync(reservation, userId))
        {
            return CancelReservationResult.Fail(CancelReservationError.Unauthorized);
        }

        // Zrušení už zrušené rezervace je idempotentní úspěch (BR-03) - zámek netřeba.
        if (reservation.State == ReservationState.Cancelled)
        {
            return CancelReservationResult.Ok(reservation.State);
        }

        var stateBeforeLock = reservation.State;

        // v0.2 (BR-03): REJECTED/EXPIRED jsou terminální, není co rušit.
        if (reservation.State is ReservationState.Rejected or ReservationState.Expired)
        {
            return CancelReservationResult.Fail(CancelReservationError.InvalidState);
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

            if (reservation.State is ReservationState.Rejected or ReservationState.Expired)
            {
                return CancelReservationResult.Fail(CancelReservationError.InvalidState);
            }

            if (stateBeforeLock == ReservationState.Draft && reservation.State == ReservationState.Confirmed)
            {
                return CancelReservationResult.Fail(CancelReservationError.Conflict);
            }

            // REQ-08 (v0.2): líná detekce vypršení schvalovacího okna i při Cancelu.
            if (HasApprovalWindowElapsed(reservation))
            {
                reservation.State = ReservationState.Expired;
                reservation.PendingApprovalSince = null;
                await _db.SaveChangesAsync();
                return CancelReservationResult.Fail(CancelReservationError.InvalidState);
            }

            // Precondition: current time < Reservation.start (BR-03).
            if (_clock.UtcNow >= reservation.StartTime)
            {
                return CancelReservationResult.Fail(CancelReservationError.TooLate);
            }

            // DRAFT, CONFIRMED nebo PENDING_APPROVAL (v0.2) -> CANCELLED (BR-03).
            // Pokud mezitím vyhrál Confirm/Approve, reservation.State je teď
            // CONFIRMED - podle BR-03 je i CONFIRMED->CANCELLED platný přechod.
            var spot = await _db.ParkingSpots.FindAsync(reservation.SpotId);
            if (reservation.State == ReservationState.Confirmed && spot?.State == ParkingSpotState.Occupied)
            {
                return CancelReservationResult.Fail(CancelReservationError.InvalidState);
            }

            reservation.State = ReservationState.Cancelled;
            if (spot is not null && spot.State == ParkingSpotState.Reserved)
            {
                spot.State = ParkingSpotState.Free;
            }
            reservation.PendingApprovalSince = null;
            await _db.SaveChangesAsync();

            return CancelReservationResult.Ok(reservation.State);
        }
        finally
        {
            spotLock.Release();
        }
    }

    /// <summary>
    /// OP-05 — Approve Reservation (approve-větev). Volá se uvnitř
    /// SpotLocks zámku z Confirm/Cancel/Approve/Reject na témže místě.
    /// </summary>
    public async Task<ApproveReservationResult> ApproveReservationAsync(int reservationId, int approverUserId)
    {
        if (!await IsActiveApproverAsync(approverUserId))
        {
            return ApproveReservationResult.Fail(ApproveReservationError.Unauthorized);
        }

        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return ApproveReservationResult.Fail(ApproveReservationError.NotFound);
        }

        var spotLock = SpotLocks.Get(reservation.SpotId);
        await spotLock.WaitAsync();
        try
        {
            await _db.Entry(reservation).ReloadAsync();

            // REQ-08: líná detekce vypršení - má přednost před ostatními kontrolami.
            if (HasApprovalWindowElapsed(reservation))
            {
                reservation.State = ReservationState.Expired;
                reservation.PendingApprovalSince = null;
                await _db.SaveChangesAsync();
                return ApproveReservationResult.Fail(ApproveReservationError.Expired);
            }

            // Precondition: Reservation X.state = PENDING_APPROVAL.
            if (reservation.State != ReservationState.PendingApproval)
            {
                return ApproveReservationResult.Fail(ApproveReservationError.InvalidState);
            }

            // Precondition: current time < Reservation X.start.
            if (_clock.UtcNow >= reservation.StartTime)
            {
                return ApproveReservationResult.Fail(ApproveReservationError.TooLate);
            }

            // Precondition: ParkingSpot is active.
            var spot = await _db.ParkingSpots.FindAsync(reservation.SpotId);
            if (spot is null || !spot.IsActive)
            {
                return ApproveReservationResult.Fail(ApproveReservationError.SpotInactive);
            }

            // BR-02: interval nesmí kolidovat s jinou CONFIRMED rezervací téhož místa.
            var overlaps = await _db.Reservations.AnyAsync(r =>
                r.Id != reservation.Id &&
                r.SpotId == reservation.SpotId &&
                r.State == ReservationState.Confirmed &&
                r.StartTime < reservation.EndTime &&
                reservation.StartTime < r.EndTime);

            if (overlaps)
            {
                return ApproveReservationResult.Fail(ApproveReservationError.Overlap);
            }

            reservation.State = ReservationState.Confirmed;
            spot.State = ParkingSpotState.Reserved;
            reservation.PendingApprovalSince = null;
            await _db.SaveChangesAsync();

            return ApproveReservationResult.Ok(reservation.State);
        }
        finally
        {
            spotLock.Release();
        }
    }

    /// <summary>OP-05 — Approve Reservation (reject-větev). Žádný overlap/active check (REQ-07).</summary>
    public async Task<RejectReservationResult> RejectReservationAsync(int reservationId, int approverUserId)
    {
        if (!await IsActiveApproverAsync(approverUserId))
        {
            return RejectReservationResult.Fail(RejectReservationError.Unauthorized);
        }

        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return RejectReservationResult.Fail(RejectReservationError.NotFound);
        }

        var spotLock = SpotLocks.Get(reservation.SpotId);
        await spotLock.WaitAsync();
        try
        {
            await _db.Entry(reservation).ReloadAsync();

            if (HasApprovalWindowElapsed(reservation))
            {
                reservation.State = ReservationState.Expired;
                reservation.PendingApprovalSince = null;
                await _db.SaveChangesAsync();
                return RejectReservationResult.Fail(RejectReservationError.Expired);
            }

            if (reservation.State != ReservationState.PendingApproval)
            {
                return RejectReservationResult.Fail(RejectReservationError.InvalidState);
            }

            // REQ-07: reject vždy uspěje, bez ohledu na overlap nebo aktivitu spotu.
            reservation.State = ReservationState.Rejected;
            reservation.PendingApprovalSince = null;
            await _db.SaveChangesAsync();

            return RejectReservationResult.Ok(reservation.State);
        }
        finally
        {
            spotLock.Release();
        }
    }

    /// <summary>REQ-08: platí jen pro PENDING_APPROVAL s nastaveným PendingApprovalSince.</summary>
    private bool HasApprovalWindowElapsed(Reservation reservation) =>
        reservation.State == ReservationState.PendingApproval &&
        reservation.PendingApprovalSince is DateTime since &&
        _clock.UtcNow - since >= _approvalWindow;

    public async Task<ParkingAreaResult> CreateParkingAreaAsync(CreateParkingAreaRequest request)
    {
        if (!await IsActiveApproverAsync(request.ApproverUserId))
        {
            return ParkingAreaResult.Fail(ParkingAreaError.Unauthorized);
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ParkingAreaResult.Fail(ParkingAreaError.InvalidName);
        }

        var area = new ParkingArea
        {
            Name = request.Name.Trim(),
            Address = request.Address,
            Type = request.Type,
            OpeningHours = request.OpeningHours,
            PricingPolicy = request.PricingPolicy
        };
        _db.ParkingAreas.Add(area);
        await _db.SaveChangesAsync();
        return ParkingAreaResult.Ok(area.Id);
    }

    public async Task<ParkingSpotResult> CreateParkingSpotAsync(int areaId, CreateParkingSpotRequest request)
    {
        if (!await IsActiveApproverAsync(request.ApproverUserId))
        {
            return ParkingSpotResult.Fail(ParkingAreaError.Unauthorized);
        }

        if (await _db.ParkingAreas.FindAsync(areaId) is null)
        {
            return ParkingSpotResult.Fail(ParkingAreaError.NotFound);
        }

        var spot = new ParkingSpot { AreaId = areaId, Type = request.Type, State = ParkingSpotState.Free };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();
        return ParkingSpotResult.Ok(spot.Id);
    }

    public async Task<ParkingAreaAvailabilityResult> CheckAreaAvailabilityAsync(int areaId, DateTime start, DateTime end)
    {
        if (start >= end)
        {
            return ParkingAreaAvailabilityResult.Fail(ParkingAreaAvailabilityError.InvalidInterval);
        }

        if (await _db.ParkingAreas.FindAsync(areaId) is null)
        {
            return ParkingAreaAvailabilityResult.Fail(ParkingAreaAvailabilityError.NotFound);
        }

        var spots = await _db.ParkingSpots
            .Where(s => s.AreaId == areaId)
            .ToListAsync();
        var result = new List<ParkingAreaAvailabilityItem>();

        foreach (var spot in spots)
        {
            var overlaps = await _db.Reservations.AnyAsync(r =>
                r.SpotId == spot.Id &&
                r.State == ReservationState.Confirmed &&
                r.StartTime < end &&
                start < r.EndTime);
            var available = spot.State is not (ParkingSpotState.Occupied or ParkingSpotState.OutOfService) && !overlaps;
            result.Add(new ParkingAreaAvailabilityItem(spot.Id, spot.Type, spot.State, available));
        }

        return ParkingAreaAvailabilityResult.Ok(result);
    }

    public Task<ParkingSpotOperationResult> SetSpotOutOfServiceAsync(int spotId, int approverUserId) =>
        SetSpotStateAsync(spotId, approverUserId, ParkingSpotState.OutOfService, ParkingSpotState.Free);

    public Task<ParkingSpotOperationResult> RestoreSpotAsync(int spotId, int approverUserId) =>
        SetSpotStateAsync(spotId, approverUserId, ParkingSpotState.Free, ParkingSpotState.OutOfService);

    public async Task<ParkingSpotOperationResult> CheckInAsync(int reservationId, int approverUserId)
    {
        if (!await IsActiveApproverAsync(approverUserId))
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.Unauthorized);
        }

        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.NotFound);
        }

        var spot = await _db.ParkingSpots.FindAsync(reservation.SpotId);
        if (reservation.State != ReservationState.Confirmed || spot is null || spot.State != ParkingSpotState.Reserved)
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.InvalidState);
        }

        spot.State = ParkingSpotState.Occupied;
        await _db.SaveChangesAsync();
        return ParkingSpotOperationResult.Ok(spot.State);
    }

    public async Task<ParkingSpotOperationResult> CheckOutAsync(int reservationId, int approverUserId)
    {
        if (!await IsActiveApproverAsync(approverUserId))
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.Unauthorized);
        }

        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.NotFound);
        }

        var spot = await _db.ParkingSpots.FindAsync(reservation.SpotId);
        if (reservation.State != ReservationState.Confirmed || spot is null || spot.State != ParkingSpotState.Occupied)
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.InvalidState);
        }

        spot.State = ParkingSpotState.Free;
        await _db.SaveChangesAsync();
        return ParkingSpotOperationResult.Ok(spot.State);
    }

    private async Task<ParkingSpotOperationResult> SetSpotStateAsync(int spotId, int approverUserId, ParkingSpotState target, ParkingSpotState expected)
    {
        if (!await IsActiveApproverAsync(approverUserId))
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.Unauthorized);
        }

        var spot = await _db.ParkingSpots.FindAsync(spotId);
        if (spot is null)
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.NotFound);
        }

        if (spot.State != expected)
        {
            return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.InvalidState);
        }

        spot.State = target;
        await _db.SaveChangesAsync();
        return ParkingSpotOperationResult.Ok(spot.State);
    }

    private async Task<bool> IsReservationOwnerDriverAsync(Reservation reservation, int userId)
    {
        if (reservation.UserId != userId)
        {
            return false;
        }

        var user = await _db.Users.FindAsync(userId);
        return user is { IsActive: true, Role: UserRole.Driver };
    }

    private async Task<bool> IsActiveApproverAsync(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        return user is { IsActive: true, Role: UserRole.Approver };
    }
}
