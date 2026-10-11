using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;
using ParkingReservation.Api.Services;

namespace ParkingReservation.Api.Tests;

/// <summary>
/// C02 v0.2 verification examples pro OP-05 Approve Reservation (approve
/// i reject větev) a pro rozšíření OP-03 (Confirm -> PENDING_APPROVAL) a
/// OP-04 (Cancel na PENDING_APPROVAL, zákaz Cancel na REJECTED/EXPIRED),
/// viz docs/specification.md, sekce 5.
/// </summary>
public class ReservationApproveRejectTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"parking-test-{Guid.NewGuid():N}.db");
    private readonly ParkingDbContext _db;
    private readonly FakeClock _clock;
    private readonly ReservationService _service;

    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan ApprovalWindow = TimeSpan.FromHours(24);

    public ReservationApproveRejectTests()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new ParkingDbContext(options);
        _db.Database.Migrate();
        _clock = new FakeClock { UtcNow = Now };
        _service = new ReservationService(_db, _clock, ApprovalWindow);
    }

    private async Task<ParkingSpot> AddApprovalSpotAsync(bool isActive = true)
    {
        var spot = new ParkingSpot { Type = ParkingSpotType.Standard, IsActive = isActive, RequiresApproval = true };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();
        return spot;
    }

    private async Task<Reservation> AddDraftAsync(int spotId, DateTime start, DateTime end, int userId = 1)
    {
        var reservation = new Reservation { SpotId = spotId, UserId = userId, StartTime = start, EndTime = end };
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();
        return reservation;
    }

    // ---------- OP-03 dodatek: Confirm na RequiresApproval spotu ----------

    [Fact]
    public async Task Confirm_on_approval_required_spot_goes_to_pending_approval_not_confirmed()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));

        var result = await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.PendingApproval, result.State);
    }

    [Fact]
    public async Task Confirm_on_approval_required_spot_ignores_existing_overlap_defers_to_approve()
    {
        // "even if another CONFIRMED Reservation already overlaps - the conflict
        // is caught later, at Approve" (OP-03 v0.2 dodatek verification example).
        var spot = await AddApprovalSpotAsync();
        await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        var confirmedBlocker = await _service.CreateReservationAsync(
            new CreateReservationRequest(1, spot.Id, Now.AddHours(1), Now.AddHours(2)));
        // Pozn.: blocker musí samo projít Confirm na non-approval spotu by nešlo,
        // tak ho rovnou nastavíme CONFIRMED přímo přes DB - simuluje existující alokaci.
        var blockerEntity = await _db.Reservations.FindAsync(confirmedBlocker.ReservationId);
        blockerEntity!.State = ReservationState.Confirmed;
        await _db.SaveChangesAsync();

        var overlappingDraft = await AddDraftAsync(spot.Id, Now.AddHours(1.5), Now.AddHours(2.5));

        var result = await _service.ConfirmReservationAsync(overlappingDraft.Id, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.PendingApproval, result.State);
    }

    // ---------- OP-05 approve ----------

    [Fact]
    public async Task Approve_pending_on_active_spot_without_overlap_succeeds()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        var result = await _service.ApproveReservationAsync(draft.Id, approverUserId: 9);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Confirmed, result.State);
    }

    [Fact]
    public async Task Approve_rejected_when_overlaps_existing_confirmed_reservation()
    {
        var spot = await AddApprovalSpotAsync();

        // Existující CONFIRMED [1h,2h) na stejném místě (nastaveno přímo přes DB).
        _db.Reservations.Add(new Reservation
        {
            SpotId = spot.Id,
            UserId = 1,
            StartTime = Now.AddHours(1),
            EndTime = Now.AddHours(2),
            State = ReservationState.Confirmed
        });
        await _db.SaveChangesAsync();

        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1.5), Now.AddHours(2.5));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        var result = await _service.ApproveReservationAsync(draft.Id, approverUserId: 9);

        Assert.False(result.Success);
        Assert.Equal(ApproveReservationError.Overlap, result.Error);
        Assert.Equal(ReservationState.PendingApproval, (await _db.Reservations.FindAsync(draft.Id))!.State);
    }

    [Fact]
    public async Task Approve_rejected_when_start_already_passed()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        _clock.UtcNow = Now.AddHours(3); // start už uplynul

        var result = await _service.ApproveReservationAsync(draft.Id, approverUserId: 9);

        Assert.False(result.Success);
        Assert.Equal(ApproveReservationError.TooLate, result.Error);
    }

    [Fact]
    public async Task Approve_rejected_on_already_confirmed_reservation()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);
        await _service.ApproveReservationAsync(draft.Id, approverUserId: 9);

        var result = await _service.ApproveReservationAsync(draft.Id, approverUserId: 9);

        Assert.False(result.Success);
        Assert.Equal(ApproveReservationError.InvalidState, result.Error);
    }

    [Fact]
    public async Task Approve_rejected_when_unauthorized()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        var result = await _service.ApproveReservationAsync(draft.Id, approverUserId: 0);

        Assert.False(result.Success);
        Assert.Equal(ApproveReservationError.Unauthorized, result.Error);
    }

    // ---------- OP-05 reject ----------

    [Fact]
    public async Task Reject_pending_succeeds_and_is_terminal()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        var result = await _service.RejectReservationAsync(draft.Id, approverUserId: 9);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Rejected, result.State);
    }

    [Fact]
    public async Task Reject_succeeds_even_when_spot_would_overlap_or_be_inactive()
    {
        // REQ-07: reject vždy uspěje bez ohledu na overlap/aktivitu (na rozdíl od approve).
        var spot = await AddApprovalSpotAsync(isActive: false);
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        // Confirm by na neaktivním spotu selhal dřív, tak PENDING_APPROVAL nastavíme přímo.
        draft.State = ReservationState.PendingApproval;
        draft.PendingApprovalSince = Now;
        await _db.SaveChangesAsync();

        var result = await _service.RejectReservationAsync(draft.Id, approverUserId: 9);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Rejected, result.State);
    }

    // ---------- REQ-08 expirace ----------

    [Fact]
    public async Task Approve_attempt_after_approval_window_elapsed_results_in_expired()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(10), Now.AddHours(11));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        _clock.UtcNow = Now + ApprovalWindow + TimeSpan.FromMinutes(1);

        var result = await _service.ApproveReservationAsync(draft.Id, approverUserId: 9);

        Assert.False(result.Success);
        Assert.Equal(ApproveReservationError.Expired, result.Error);
        Assert.Equal(ReservationState.Expired, (await _db.Reservations.FindAsync(draft.Id))!.State);
    }

    // ---------- OP-04 dodatek: Cancel na PENDING_APPROVAL / REJECTED / EXPIRED ----------

    [Fact]
    public async Task Cancel_pending_approval_before_start_succeeds()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        var result = await _service.CancelReservationAsync(draft.Id, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Cancelled, result.State);
    }

    [Fact]
    public async Task Cancel_rejected_on_rejected_reservation()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);
        await _service.RejectReservationAsync(draft.Id, approverUserId: 9);

        var result = await _service.CancelReservationAsync(draft.Id, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(CancelReservationError.InvalidState, result.Error);
    }

    [Fact]
    public async Task Cancel_rejected_on_expired_reservation()
    {
        var spot = await AddApprovalSpotAsync();
        var draft = await AddDraftAsync(spot.Id, Now.AddHours(10), Now.AddHours(11));
        await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        _clock.UtcNow = Now + ApprovalWindow + TimeSpan.FromMinutes(1);

        var result = await _service.CancelReservationAsync(draft.Id, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(CancelReservationError.InvalidState, result.Error);
        Assert.Equal(ReservationState.Expired, (await _db.Reservations.FindAsync(draft.Id))!.State);
    }

    public void Dispose()
    {
        _db.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
