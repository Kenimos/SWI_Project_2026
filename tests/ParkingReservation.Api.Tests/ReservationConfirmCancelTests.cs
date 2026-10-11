using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;
using ParkingReservation.Api.Services;

namespace ParkingReservation.Api.Tests;

/// <summary>
/// C02 verification examples pro OP-03 Confirm Reservation a OP-04 Cancel
/// Reservation (viz docs/specification.md). "Current time" je řízený přes
/// FakeClock, aby byly testy nezávislé na reálném datu.
/// </summary>
public class ReservationConfirmCancelTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"parking-test-{Guid.NewGuid():N}.db");
    private readonly ParkingDbContext _db;
    private readonly FakeClock _clock;
    private readonly ReservationService _service;

    // Pevný referenční okamžik "teď", vůči kterému jsou start/end testů počítané.
    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public ReservationConfirmCancelTests()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new ParkingDbContext(options);
        _db.Database.Migrate();
        _clock = new FakeClock { UtcNow = Now };
        _service = new ReservationService(_db, _clock);
    }

    private async Task<ParkingSpot> AddSpotAsync(bool isActive = true, ParkingSpotType type = ParkingSpotType.Standard)
    {
        var spot = new ParkingSpot { Type = type, IsActive = isActive };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();
        return spot;
    }

    private async Task<Reservation> AddReservationAsync(
        int spotId, DateTime start, DateTime end, ReservationState state = ReservationState.Draft, int userId = 1)
    {
        var reservation = new Reservation
        {
            SpotId = spotId,
            UserId = userId,
            StartTime = start,
            EndTime = end,
            State = state
        };
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync();
        return reservation;
    }

    // ---------- OP-03 Confirm Reservation ----------

    [Fact]
    public async Task Confirm_draft_on_active_spot_without_overlap_succeeds()
    {
        var spot = await AddSpotAsync();
        var reservation = await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));

        var result = await _service.ConfirmReservationAsync(reservation.Id, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Confirmed, result.State);
    }

    [Fact]
    public async Task Confirm_rejected_when_overlaps_existing_confirmed_reservation()
    {
        var spot = await AddSpotAsync();
        await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2), ReservationState.Confirmed);
        var draft = await AddReservationAsync(spot.Id, Now.AddHours(1.5), Now.AddHours(2.5));

        var result = await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(ConfirmReservationError.Overlap, result.Error);
        Assert.Equal(ReservationState.Draft, (await _db.Reservations.FindAsync(draft.Id))!.State);
    }

    [Fact]
    public async Task Confirm_succeeds_when_intervals_only_touch_at_boundary()
    {
        // BR-01: [start,end) - konec existující CONFIRMED (2:00) se nepřekrývá se začátkem nové (2:00).
        var spot = await AddSpotAsync();
        await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2), ReservationState.Confirmed);
        var draft = await AddReservationAsync(spot.Id, Now.AddHours(2), Now.AddHours(3));

        var result = await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Confirmed, result.State);
    }

    [Fact]
    public async Task Confirm_rejected_on_inactive_spot()
    {
        var spot = await AddSpotAsync(isActive: false);
        var draft = await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));

        var result = await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(ConfirmReservationError.SpotInactive, result.Error);
    }

    [Fact]
    public async Task Confirm_rejected_when_already_confirmed()
    {
        var spot = await AddSpotAsync();
        var reservation = await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2), ReservationState.Confirmed);

        var result = await _service.ConfirmReservationAsync(reservation.Id, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(ConfirmReservationError.InvalidState, result.Error);
    }

    [Fact]
    public async Task Confirm_rejected_when_start_already_passed()
    {
        var spot = await AddSpotAsync();
        var draft = await AddReservationAsync(spot.Id, Now.AddHours(-1), Now.AddHours(1));

        var result = await _service.ConfirmReservationAsync(draft.Id, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(ConfirmReservationError.TooLate, result.Error);
    }

    [Fact]
    public async Task Confirm_rejected_when_unauthorized()
    {
        var spot = await AddSpotAsync();
        var draft = await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));

        var result = await _service.ConfirmReservationAsync(draft.Id, userId: 0);

        Assert.False(result.Success);
        Assert.Equal(ConfirmReservationError.Unauthorized, result.Error);
    }

    [Fact]
    public async Task Concurrent_conflicting_confirms_on_same_spot_exactly_one_succeeds()
    {
        // REQ-04: "Two concurrent conflicting DRAFTs on the same ParkingSpot
        // -> exactly one CONFIRMED, the other rejected and DRAFT."
        var spot = await AddSpotAsync();
        var draftA = await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        var draftB = await AddReservationAsync(spot.Id, Now.AddHours(1.5), Now.AddHours(2.5));

        // Každé "souběžné" volání má vlastní DbContext/service, stejně jako
        // dva nezávislé HTTP požadavky v ASP.NET Core (scoped DbContext).
        var optionsA = new DbContextOptionsBuilder<ParkingDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        var optionsB = new DbContextOptionsBuilder<ParkingDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        using var dbA = new ParkingDbContext(optionsA);
        using var dbB = new ParkingDbContext(optionsB);
        var serviceA = new ReservationService(dbA, _clock);
        var serviceB = new ReservationService(dbB, _clock);

        var taskA = serviceA.ConfirmReservationAsync(draftA.Id, userId: 1);
        var taskB = serviceB.ConfirmReservationAsync(draftB.Id, userId: 1);
        await Task.WhenAll(taskA, taskB);

        var results = new[] { taskA.Result, taskB.Result };
        Assert.Single(results, r => r.Success && r.State == ReservationState.Confirmed);
        Assert.Single(results, r => !r.Success && r.Error == ConfirmReservationError.Overlap);
    }

    // ---------- OP-04 Cancel Reservation ----------

    [Fact]
    public async Task Cancel_draft_before_start_succeeds()
    {
        var spot = await AddSpotAsync();
        var draft = await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));

        var result = await _service.CancelReservationAsync(draft.Id, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Cancelled, result.State);
    }

    [Fact]
    public async Task Cancel_confirmed_before_start_succeeds_and_frees_the_spot()
    {
        var spot = await AddSpotAsync();
        var confirmed = await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2), ReservationState.Confirmed);

        var cancelResult = await _service.CancelReservationAsync(confirmed.Id, userId: 1);
        Assert.True(cancelResult.Success);
        Assert.Equal(ReservationState.Cancelled, cancelResult.State);

        // Postcondition: "no longer blocks its ParkingSpot" - ověřeno přes OP-02.
        var availability = await _service.CheckAvailabilityAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));
        Assert.True(availability.Available);
    }

    [Fact]
    public async Task Cancel_already_cancelled_is_idempotent_even_after_start()
    {
        var spot = await AddSpotAsync();
        var reservation = await AddReservationAsync(spot.Id, Now.AddHours(-2), Now.AddHours(-1), ReservationState.Cancelled);

        var result = await _service.CancelReservationAsync(reservation.Id, userId: 1);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Cancelled, result.State);
    }

    [Fact]
    public async Task Cancel_rejected_when_start_already_passed()
    {
        var spot = await AddSpotAsync();
        var draft = await AddReservationAsync(spot.Id, Now.AddHours(-1), Now.AddHours(1));

        var result = await _service.CancelReservationAsync(draft.Id, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(CancelReservationError.TooLate, result.Error);
        Assert.Equal(ReservationState.Draft, (await _db.Reservations.FindAsync(draft.Id))!.State);
    }

    [Fact]
    public async Task Cancel_rejected_when_unauthorized()
    {
        var spot = await AddSpotAsync();
        var draft = await AddReservationAsync(spot.Id, Now.AddHours(1), Now.AddHours(2));

        var result = await _service.CancelReservationAsync(draft.Id, userId: 0);

        Assert.False(result.Success);
        Assert.Equal(CancelReservationError.Unauthorized, result.Error);
    }

    [Fact]
    public async Task Cancel_rejected_for_unknown_reservation()
    {
        var result = await _service.CancelReservationAsync(999, userId: 1);

        Assert.False(result.Success);
        Assert.Equal(CancelReservationError.NotFound, result.Error);
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
