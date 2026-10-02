using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;
using ParkingReservation.Api.Services;

namespace ParkingReservation.Api.Tests;

/// <summary>
/// C02 verification examples pro OP-01 Create Reservation a OP-02 Check
/// Availability (viz docs/specification.md). Každý test běží proti vlastnímu
/// dočasnému souboru .db se schématem vytvořeným přes skutečné EF migrace.
/// </summary>
public class ReservationServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"parking-test-{Guid.NewGuid():N}.db");
    private readonly ParkingDbContext _db;
    private readonly ReservationService _service;

    public ReservationServiceTests()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new ParkingDbContext(options);
        _db.Database.Migrate();
        _service = new ReservationService(_db);
    }

    private async Task<ParkingSpot> AddSpotAsync(ParkingSpotType type = ParkingSpotType.Standard)
    {
        var spot = new ParkingSpot { Type = type };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();
        return spot;
    }

    // ---------- OP-01 Create Reservation ----------

    [Fact]
    public async Task Create_valid_standard_spot_and_interval_creates_one_draft()
    {
        var spot = await AddSpotAsync();
        var request = new CreateReservationRequest(UserId: 1, SpotId: spot.Id,
            Start: new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            End: new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc));

        var result = await _service.CreateReservationAsync(request);

        Assert.True(result.Success);
        Assert.Equal(ReservationState.Draft, result.State);
        Assert.Single(_db.Reservations);
    }

    [Fact]
    public async Task Create_rejects_when_start_equals_end()
    {
        var spot = await AddSpotAsync();
        var sameInstant = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        var request = new CreateReservationRequest(UserId: 1, SpotId: spot.Id, Start: sameInstant, End: sameInstant);

        var result = await _service.CreateReservationAsync(request);

        Assert.False(result.Success);
        Assert.Equal(CreateReservationError.InvalidInterval, result.Error);
        Assert.Empty(_db.Reservations);
    }

    [Fact]
    public async Task Create_rejects_unknown_spot()
    {
        var request = new CreateReservationRequest(UserId: 1, SpotId: 999,
            Start: new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            End: new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc));

        var result = await _service.CreateReservationAsync(request);

        Assert.False(result.Success);
        Assert.Equal(CreateReservationError.SpotNotFound, result.Error);
    }

    [Fact]
    public async Task Create_rejects_ev_charging_spot_for_non_electric_vehicle()
    {
        var spot = await AddSpotAsync(ParkingSpotType.EvCharging);
        var request = new CreateReservationRequest(UserId: 1, SpotId: spot.Id,
            Start: new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            End: new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            VehicleIsElectric: false);

        var result = await _service.CreateReservationAsync(request);

        Assert.False(result.Success);
        Assert.Equal(CreateReservationError.SpotRequiresEligibility, result.Error);
    }

    [Fact]
    public async Task Create_accepts_ev_charging_spot_for_electric_vehicle()
    {
        var spot = await AddSpotAsync(ParkingSpotType.EvCharging);
        var request = new CreateReservationRequest(UserId: 1, SpotId: spot.Id,
            Start: new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            End: new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            VehicleIsElectric: true);

        var result = await _service.CreateReservationAsync(request);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Create_rejects_unauthorized_user()
    {
        var spot = await AddSpotAsync();
        var request = new CreateReservationRequest(UserId: 0, SpotId: spot.Id,
            Start: new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            End: new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc));

        var result = await _service.CreateReservationAsync(request);

        Assert.False(result.Success);
        Assert.Equal(CreateReservationError.Unauthorized, result.Error);
    }

    // ---------- OP-02 Check Availability ----------
    // Verification examples přesně podle docs/specification.md:
    // Existing CONFIRMED: [10:00,11:00)
    // query [09:00,10:00) -> AVAILABLE
    // query [10:30,11:30) -> UNAVAILABLE
    // query [11:00,12:00) -> AVAILABLE

    private async Task<ParkingSpot> AddSpotWithConfirmedReservationAsync()
    {
        var spot = await AddSpotAsync();
        _db.Reservations.Add(new Reservation
        {
            SpotId = spot.Id,
            UserId = 1,
            StartTime = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            State = ReservationState.Confirmed
        });
        await _db.SaveChangesAsync();
        return spot;
    }

    [Fact]
    public async Task Availability_before_confirmed_reservation_is_available()
    {
        var spot = await AddSpotWithConfirmedReservationAsync();

        var result = await _service.CheckAvailabilityAsync(spot.Id,
            new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc));

        Assert.True(result.Success);
        Assert.True(result.Available);
    }

    [Fact]
    public async Task Availability_overlapping_confirmed_reservation_is_unavailable()
    {
        var spot = await AddSpotWithConfirmedReservationAsync();

        var result = await _service.CheckAvailabilityAsync(spot.Id,
            new DateTime(2026, 10, 1, 10, 30, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 11, 30, 0, DateTimeKind.Utc));

        Assert.True(result.Success);
        Assert.False(result.Available);
    }

    [Fact]
    public async Task Availability_right_after_confirmed_reservation_is_available()
    {
        // Half-open interval [start,end) - konec 11:00 už do rezervace nepatří.
        var spot = await AddSpotWithConfirmedReservationAsync();

        var result = await _service.CheckAvailabilityAsync(spot.Id,
            new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc));

        Assert.True(result.Success);
        Assert.True(result.Available);
    }

    [Fact]
    public async Task Availability_ignores_draft_reservations()
    {
        // DRAFT neblokuje dostupnost - jen CONFIRMED (viz Assumption v OP-02).
        var spot = await AddSpotAsync();
        _db.Reservations.Add(new Reservation
        {
            SpotId = spot.Id,
            UserId = 1,
            StartTime = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc),
            State = ReservationState.Draft
        });
        await _db.SaveChangesAsync();

        var result = await _service.CheckAvailabilityAsync(spot.Id,
            new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc));

        Assert.True(result.Success);
        Assert.True(result.Available);
    }

    [Fact]
    public async Task Availability_rejects_unknown_spot()
    {
        var result = await _service.CheckAvailabilityAsync(999,
            new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 10, 1, 11, 0, 0, DateTimeKind.Utc));

        Assert.False(result.Success);
        Assert.Equal(AvailabilityError.SpotNotFound, result.Error);
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
