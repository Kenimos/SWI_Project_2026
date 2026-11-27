using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Services;

/// <summary>
/// Implements OP-01 to OP-07 according to docs/specification.md (Baseline v0.2.1).
/// The single owner of all Reservation and ParkingSpot state transitions (ADR-006,
/// scripts/check-architecture-conformance.sh).
/// </summary>
public class ReservationService
{
    private readonly ParkingDbContext _db;
    private readonly IClock _clock;
    private readonly TimeSpan _approvalWindow;

    /// <param name="approvalWindow">
    /// v0.2 - how long PENDING_APPROVAL may wait before it is treated as
    /// EXPIRED on the next access (Approve/Reject/Cancel) (REQ-08).
    /// The exact value is explicitly TBD in docs/specification.md - this is
    /// only a demonstration placeholder, not an agreed business rule.
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

        var start = ToUtc(request.Start);
        var end = ToUtc(request.End);

        // Precondition: start < end (BR-01).
        if (start >= end)
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

        // Success postcondition: a new Reservation in state DRAFT, the spot is not
        // allocated yet (Create per OP-01 does not check conflicts, see BR-02).
        var reservation = new Reservation
        {
            SpotId = spot.Id,
            UserId = request.UserId,
            StartTime = start,
            EndTime = end,
            State = ReservationState.Draft
        };

        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();

        return CreateReservationResult.Ok(reservation.Id, reservation.State);
    }

    /// <summary>OP-02 — Check Availability.</summary>
    public async Task<AvailabilityResult> CheckAvailabilityAsync(int spotId, DateTime start, DateTime end)
    {
        start = ToUtc(start);
        end = ToUtc(end);
        if (start >= end)
        {
            return AvailabilityResult.Fail(AvailabilityError.InvalidInterval);
        }

        var spot = await _db.ParkingSpots.FindAsync(spotId);
        if (spot is null)
        {
            return AvailabilityResult.Fail(AvailabilityError.SpotNotFound);
        }

        // REQ-02: the same rule as area availability (one method).
        return AvailabilityResult.Ok(await IsSpotAvailableAsync(spot, start, end));
    }

    /// <summary>
    /// OP-03 — Confirm Reservation. Concurrent attempts to confirm reservations
    /// of the same spot (REQ-04) and a Confirm/Cancel race on the same reservation
    /// (BR-03) are serialized by the SpotLocks lock of that ParkingSpot.
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
            // Fresh state from the DB, not what we saw before acquiring the lock.
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

            // BR-05 (v0.2): spots requiring approval go to PENDING_APPROVAL
            // instead of directly to CONFIRMED - the overlap check is deferred to Approve.
            if (spot.RequiresApproval)
            {
                reservation.State = ReservationState.PendingApproval;
                reservation.PendingApprovalSince = _clock.UtcNow;
                // PENDING_APPROVAL does not block the spot (OP-02 v0.2) - the spot state does not change.
                await _db.SaveChangesAsync();

                return ConfirmReservationResult.Ok(reservation.State);
            }

            // REQ-03: the interval must not collide with another CONFIRMED reservation
            // of the same spot (BR-01, BR-02).
            var overlaps = await HasBlockingOverlapAsync(
                reservation.SpotId, reservation.StartTime, reservation.EndTime, excludeReservationId: reservation.Id);

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
        var reservation = await _db.Reservations.FindAsync(reservationId);
        if (reservation is null)
        {
            return CancelReservationResult.Fail(CancelReservationError.NotFound);
        }

        if (!await IsReservationOwnerDriverAsync(reservation, userId))
        {
            return CancelReservationResult.Fail(CancelReservationError.Unauthorized);
        }

        // Cancelling an already cancelled reservation is an idempotent success (BR-03) - no lock needed.
        if (reservation.State == ReservationState.Cancelled)
        {
            return CancelReservationResult.Ok(reservation.State);
        }

        var stateBeforeLock = reservation.State;

        // v0.2 (BR-03): REJECTED/EXPIRED/COMPLETED are terminal, nothing left to cancel.
        if (IsTerminalNonCancelled(reservation.State))
        {
            return CancelReservationResult.Fail(CancelReservationError.InvalidState);
        }

        var spotLock = SpotLocks.Get(reservation.SpotId);
        await spotLock.WaitAsync();
        try
        {
            await _db.Entry(reservation).ReloadAsync();

            // It may have become CANCELLED meanwhile (before we got the lock) by a concurrent Cancel.
            if (reservation.State == ReservationState.Cancelled)
            {
                return CancelReservationResult.Ok(reservation.State);
            }

            if (IsTerminalNonCancelled(reservation.State))
            {
                return CancelReservationResult.Fail(CancelReservationError.InvalidState);
            }

            if (stateBeforeLock == ReservationState.Draft && reservation.State == ReservationState.Confirmed)
            {
                return CancelReservationResult.Fail(CancelReservationError.Conflict);
            }

            // REQ-08 (v0.2): lazy detection of the approval window expiry on Cancel as well.
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

            // DRAFT, CONFIRMED or PENDING_APPROVAL (v0.2) -> CANCELLED (BR-03).
            // Check-in is possible only from start (BR-07), so before start the reservation
            // is never physically occupied and the spot state is not touched here.
            reservation.State = ReservationState.Cancelled;
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
    /// OP-05 — Approve Reservation (approve branch). Runs inside the
    /// SpotLocks lock shared by Confirm/Cancel/Approve/Reject on the same spot.
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

            // REQ-08: lazy expiry detection - takes precedence over the other checks.
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

            // BR-02: the interval must not collide with another CONFIRMED reservation of the same spot.
            var overlaps = await HasBlockingOverlapAsync(
                reservation.SpotId, reservation.StartTime, reservation.EndTime, excludeReservationId: reservation.Id);

            if (overlaps)
            {
                return ApproveReservationResult.Fail(ApproveReservationError.Overlap);
            }

            reservation.State = ReservationState.Confirmed;
            reservation.PendingApprovalSince = null;
            await _db.SaveChangesAsync();

            return ApproveReservationResult.Ok(reservation.State);
        }
        finally
        {
            spotLock.Release();
        }
    }

    /// <summary>OP-05 — Approve Reservation (reject branch). No overlap/active check (REQ-07).</summary>
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

            // REQ-07: reject always succeeds, regardless of overlap or spot activity.
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

    /// <summary>REQ-08: applies only to PENDING_APPROVAL with PendingApprovalSince set.</summary>
    private bool HasApprovalWindowElapsed(Reservation reservation) =>
        reservation.State == ReservationState.PendingApproval &&
        reservation.PendingApprovalSince is DateTime since &&
        _clock.UtcNow - since >= _approvalWindow;

    private static bool IsTerminalNonCancelled(ReservationState state) =>
        state is ReservationState.Rejected or ReservationState.Expired or ReservationState.Completed;

    /// <summary>
    /// All times are UTC (section 0 of the specification). A time with an offset is
    /// converted; a time without a zone is treated as UTC.
    /// </summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    /// <summary>BR-02: does the interval [start, end) overlap any CONFIRMED reservation of the spot? (BR-01)</summary>
    private Task<bool> HasBlockingOverlapAsync(int spotId, DateTime start, DateTime end, int? excludeReservationId = null) =>
        _db.Reservations.AnyAsync(r =>
            r.SpotId == spotId &&
            (excludeReservationId == null || r.Id != excludeReservationId) &&
            r.State == ReservationState.Confirmed &&
            r.StartTime < end &&
            start < r.EndTime);

    /// <summary>
    /// The single availability rule for OP-02 and area availability (REQ-02, BR-06, BR-07):
    /// an out-of-service spot is always unavailable; a physically occupied spot is unavailable
    /// for an interval containing the current time; otherwise overlap with CONFIRMED reservations decides.
    /// </summary>
    private async Task<bool> IsSpotAvailableAsync(ParkingSpot spot, DateTime start, DateTime end)
    {
        if (!spot.IsActive)
        {
            return false;
        }

        var now = _clock.UtcNow;
        if (spot.State == ParkingSpotState.Occupied && start <= now && now < end)
        {
            return false;
        }

        return !await HasBlockingOverlapAsync(spot.Id, start, end);
    }

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
        start = ToUtc(start);
        end = ToUtc(end);
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
            var available = await IsSpotAvailableAsync(spot, start, end);

            // State for the given interval: stored OutOfService/Occupied takes precedence,
            // Reserved is derived from an overlap with a CONFIRMED reservation.
            var reportedState = spot.State;
            if (reportedState == ParkingSpotState.Free && await HasBlockingOverlapAsync(spot.Id, start, end))
            {
                reportedState = ParkingSpotState.Reserved;
            }

            result.Add(new ParkingAreaAvailabilityItem(spot.Id, spot.Type, reportedState, available));
        }

        return ParkingAreaAvailabilityResult.Ok(result);
    }

    public Task<ParkingSpotOperationResult> SetSpotOutOfServiceAsync(int spotId, int approverUserId) =>
        SetSpotStateAsync(spotId, approverUserId, ParkingSpotState.OutOfService, ParkingSpotState.Free);

    public Task<ParkingSpotOperationResult> RestoreSpotAsync(int spotId, int approverUserId) =>
        SetSpotStateAsync(spotId, approverUserId, ParkingSpotState.Free, ParkingSpotState.OutOfService);

    /// <summary>
    /// OP-07 — Check-in (BR-07): the Approver records the arrival of the vehicle for a specific
    /// CONFIRMED reservation within its window [start, end).
    /// </summary>
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

        var spotLock = SpotLocks.Get(reservation.SpotId);
        await spotLock.WaitAsync();
        try
        {
            await _db.Entry(reservation).ReloadAsync();

            if (reservation.State != ReservationState.Confirmed || reservation.CheckedInAt is not null)
            {
                return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.InvalidState);
            }

            var now = _clock.UtcNow;
            if (now < reservation.StartTime || now >= reservation.EndTime)
            {
                return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.OutsideReservationWindow);
            }

            var spot = await _db.ParkingSpots.FindAsync(reservation.SpotId);
            if (spot is null || spot.State != ParkingSpotState.Free)
            {
                return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.SpotUnavailable);
            }

            reservation.CheckedInAt = now;
            spot.State = ParkingSpotState.Occupied;
            await _db.SaveChangesAsync();
            return ParkingSpotOperationResult.Ok(spot.State);
        }
        finally
        {
            spotLock.Release();
        }
    }

    /// <summary>
    /// OP-07 — Check-out (BR-07): departure of the vehicle; the reservation moves to COMPLETED
    /// and stops blocking the rest of its window, the spot is FREE again.
    /// </summary>
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

        var spotLock = SpotLocks.Get(reservation.SpotId);
        await spotLock.WaitAsync();
        try
        {
            await _db.Entry(reservation).ReloadAsync();

            var spot = await _db.ParkingSpots.FindAsync(reservation.SpotId);
            if (reservation.State != ReservationState.Confirmed ||
                reservation.CheckedInAt is null ||
                spot is null ||
                spot.State != ParkingSpotState.Occupied)
            {
                return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.InvalidState);
            }

            reservation.State = ReservationState.Completed;
            spot.State = ParkingSpotState.Free;
            await _db.SaveChangesAsync();
            return ParkingSpotOperationResult.Ok(spot.State);
        }
        finally
        {
            spotLock.Release();
        }
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

        // The same lock as Confirm/Approve, which read IsActive (BR-06).
        var spotLock = SpotLocks.Get(spotId);
        await spotLock.WaitAsync();
        try
        {
            await _db.Entry(spot).ReloadAsync();

            // OUT_OF_SERVICE only from FREE (not while physically occupied), restore only from OUT_OF_SERVICE.
            if (spot.State != expected)
            {
                return ParkingSpotOperationResult.Fail(ParkingSpotOperationError.InvalidState);
            }

            spot.State = target;
            await _db.SaveChangesAsync();
            return ParkingSpotOperationResult.Ok(spot.State);
        }
        finally
        {
            spotLock.Release();
        }
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
