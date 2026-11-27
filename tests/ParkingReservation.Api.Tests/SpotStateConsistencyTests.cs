using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;
using ParkingReservation.Api.Services;

namespace ParkingReservation.Api.Tests;

/// <summary>
/// Regression tests for the internal audit of the spot state (spec v0.2.1, BR-06 and BR-07, OP-06 and OP-07).
/// Each test corresponds to one defect found on commit 8a5b312.
/// </summary>
public class SpotStateConsistencyTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"parking-spotstate-{Guid.NewGuid():N}.db");
    private readonly ParkingDbContext _db;
    private readonly FakeClock _clock;
    private readonly ReservationService _service;
    private readonly int _areaId;

    private static readonly DateTime Now = new(2030, 1, 1, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ten = new(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    public SpotStateConsistencyTests()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new ParkingDbContext(options);
        _db.Database.Migrate();
        _db.Users.AddRange(
            new User { Id = 1, Role = UserRole.Driver },
            new User { Id = 9, Role = UserRole.Approver });
        var area = new ParkingArea { Name = "Audit area" };
        _db.ParkingAreas.Add(area);
        _db.SaveChanges();
        _areaId = area.Id;
        _clock = new FakeClock { UtcNow = Now };
        _service = new ReservationService(_db, _clock, TimeSpan.FromHours(24));
    }

    private async Task<int> NewSpotAsync(bool requiresApproval = false)
    {
        var spot = new ParkingSpot { AreaId = _areaId, RequiresApproval = requiresApproval };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();
        return spot.Id;
    }

    private async Task<int> ConfirmedAsync(int spotId, DateTime start, DateTime end)
    {
        var created = await _service.CreateReservationAsync(new CreateReservationRequest(1, spotId, start, end));
        var confirmed = await _service.ConfirmReservationAsync(created.ReservationId!.Value, 1);
        Assert.True(confirmed.Success);
        return created.ReservationId.Value;
    }

    private async Task<ParkingSpot> ReadSpotAsync(int spotId)
    {
        await using var read = new ParkingDbContext(new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        return await read.ParkingSpots.AsNoTracking().SingleAsync(s => s.Id == spotId);
    }

    private async Task<Reservation> ReadReservationAsync(int reservationId)
    {
        await using var read = new ParkingDbContext(new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite($"Data Source={_dbPath}").Options);
        return await read.Reservations.AsNoTracking().SingleAsync(r => r.Id == reservationId);
    }

    [Fact] // BR-06: OUT_OF_SERVICE == inactive (previously it could be reserved and Confirm overwrote the state)
    public async Task Out_of_service_spot_is_unavailable_and_cannot_be_confirmed()
    {
        var spotId = await NewSpotAsync();
        Assert.True((await _service.SetSpotOutOfServiceAsync(spotId, 9)).Success);
        var created = await _service.CreateReservationAsync(new CreateReservationRequest(1, spotId, Ten, Ten.AddHours(1)));

        var availability = await _service.CheckAvailabilityAsync(spotId, Ten, Ten.AddHours(1));
        var confirm = await _service.ConfirmReservationAsync(created.ReservationId!.Value, 1);

        Assert.False(availability.Available);
        Assert.Equal(ConfirmReservationError.SpotInactive, confirm.Error);
        Assert.Equal(ParkingSpotState.OutOfService, (await ReadSpotAsync(spotId)).State);
        Assert.Equal(ReservationState.Draft, (await ReadReservationAsync(created.ReservationId.Value)).State);
        Assert.True((await _service.RestoreSpotAsync(spotId, 9)).Success);
    }

    [Fact] // REQ-02: OP-02 and area availability use the same rule
    public async Task Spot_and_area_availability_agree_and_area_reports_reserved()
    {
        var freeSpot = await NewSpotAsync();
        var bookedSpot = await NewSpotAsync();
        var outOfServiceSpot = await NewSpotAsync();
        await ConfirmedAsync(bookedSpot, Ten, Ten.AddHours(1));
        await _service.SetSpotOutOfServiceAsync(outOfServiceSpot, 9);

        var area = await _service.CheckAreaAvailabilityAsync(_areaId, Ten.AddMinutes(30), Ten.AddHours(2));

        foreach (var item in area.Spots)
        {
            var single = await _service.CheckAvailabilityAsync(item.SpotId, Ten.AddMinutes(30), Ten.AddHours(2));
            Assert.Equal(single.Available, item.Available);
        }
        Assert.Contains(area.Spots, s => s.SpotId == freeSpot && s.Available && s.State == ParkingSpotState.Free);
        Assert.Contains(area.Spots, s => s.SpotId == bookedSpot && !s.Available && s.State == ParkingSpotState.Reserved);
        Assert.Contains(area.Spots, s => s.SpotId == outOfServiceSpot && !s.Available && s.State == ParkingSpotState.OutOfService);
    }

    [Fact] // BR-07: cancelling one reservation must not break check-in of another confirmed reservation of the spot
    public async Task Cancelling_one_reservation_keeps_check_in_of_another_possible()
    {
        var spotId = await NewSpotAsync();
        var first = await ConfirmedAsync(spotId, Ten, Ten.AddHours(1));
        var second = await ConfirmedAsync(spotId, Ten.AddHours(2), Ten.AddHours(3));

        Assert.True((await _service.CancelReservationAsync(first, 1)).Success);
        _clock.UtcNow = Ten.AddHours(2).AddMinutes(1);
        var checkIn = await _service.CheckInAsync(second, 9);

        Assert.True(checkIn.Success);
        Assert.Equal(ParkingSpotState.Occupied, (await ReadSpotAsync(spotId)).State);
    }

    [Fact] // OP-02 v0.2: neither PENDING_APPROVAL nor REJECTED locks the spot
    public async Task Pending_and_rejected_reservations_do_not_lock_spot()
    {
        var spotId = await NewSpotAsync(requiresApproval: true);
        var created = await _service.CreateReservationAsync(new CreateReservationRequest(1, spotId, Ten, Ten.AddHours(1)));
        await _service.ConfirmReservationAsync(created.ReservationId!.Value, 1);
        Assert.Equal(ParkingSpotState.Free, (await ReadSpotAsync(spotId)).State);

        await _service.RejectReservationAsync(created.ReservationId.Value, 9);
        var outOfService = await _service.SetSpotOutOfServiceAsync(spotId, 9);

        Assert.True(outOfService.Success);
    }

    [Theory] // BR-07: check-in only within the window [start, end)
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(59, true)]
    [InlineData(60, false)]
    public async Task Check_in_only_inside_reservation_window(int minutesFromStart, bool expectedSuccess)
    {
        var spotId = await NewSpotAsync();
        var reservationId = await ConfirmedAsync(spotId, Ten, Ten.AddHours(1));
        _clock.UtcNow = Ten.AddMinutes(minutesFromStart);

        var result = await _service.CheckInAsync(reservationId, 9);

        Assert.Equal(expectedSuccess, result.Success);
        if (!expectedSuccess)
        {
            Assert.Equal(ParkingSpotOperationError.OutsideReservationWindow, result.Error);
            Assert.Equal(ParkingSpotState.Free, (await ReadSpotAsync(spotId)).State);
            Assert.Null((await ReadReservationAsync(reservationId)).CheckedInAt);
        }
    }

    [Fact] // BR-07: after check-out the reservation does not block the rest of the window; a second check-in is not allowed
    public async Task Check_out_completes_reservation_and_releases_rest_of_window()
    {
        var spotId = await NewSpotAsync();
        var reservationId = await ConfirmedAsync(spotId, Ten, Ten.AddHours(2));
        _clock.UtcNow = Ten.AddMinutes(10);
        Assert.True((await _service.CheckInAsync(reservationId, 9)).Success);
        Assert.Equal(ParkingSpotOperationError.InvalidState, (await _service.CheckInAsync(reservationId, 9)).Error);

        _clock.UtcNow = Ten.AddMinutes(40);
        Assert.True((await _service.CheckOutAsync(reservationId, 9)).Success);
        var availability = await _service.CheckAvailabilityAsync(spotId, Ten.AddHours(1), Ten.AddHours(2));

        Assert.True(availability.Available);
        Assert.Equal(ReservationState.Completed, (await ReadReservationAsync(reservationId)).State);
        Assert.Equal(ParkingSpotState.Free, (await ReadSpotAsync(spotId)).State);
        Assert.Equal(CancelReservationError.InvalidState, (await _service.CancelReservationAsync(reservationId, 1)).Error);
    }

    [Fact] // Section 0: times with an offset are stored converted to UTC
    public async Task Create_normalizes_local_time_to_utc()
    {
        var spotId = await NewSpotAsync();
        var localStart = new DateTime(2030, 6, 1, 12, 0, 0, DateTimeKind.Local);

        var created = await _service.CreateReservationAsync(new CreateReservationRequest(
            1, spotId, localStart, localStart.AddHours(1)));

        var stored = await ReadReservationAsync(created.ReservationId!.Value);
        Assert.Equal(localStart.ToUniversalTime(), DateTime.SpecifyKind(stored.StartTime, DateTimeKind.Utc));
    }

    [Fact] // Migration: IsActive=0 -> OutOfService, Reserved -> Free, valid check-in kept

    public async Task Migration_backfills_spot_state_and_check_in()
    {
        var path = Path.Combine(Path.GetTempPath(), $"parking-migration-{Guid.NewGuid():N}.db");
        try
        {
            await using (var db = new ParkingDbContext(new DbContextOptionsBuilder<ParkingDbContext>()
                .UseSqlite($"Data Source={path}").Options))
            {
                await db.GetService<IMigrator>().MigrateAsync("20261025151543_AddUsersParkingAreasAndSpotState");
                await db.Database.ExecuteSqlRawAsync(@"
INSERT INTO Users (Id, Role, IsActive) VALUES (1, 'Driver', 1);
INSERT INTO ParkingAreas (Id, Name, Type) VALUES (1, 'A', 'Garage');
INSERT INTO ParkingSpots (Id, AreaId, IsActive, RequiresApproval, State, Type) VALUES
  (1, 1, 0, 0, 'Free', 'Standard'),
  (2, 1, 1, 0, 'Reserved', 'Standard'),
  (3, 1, 1, 0, 'Occupied', 'Standard'),
  (4, 1, 1, 0, 'Occupied', 'Standard');
INSERT INTO Reservations (Id, SpotId, UserId, StartTime, EndTime, State) VALUES
  (1, 3, 1, '2020-01-01 10:00:00', '2099-01-01 10:00:00', 'Confirmed'),
  (2, 4, 1, '2099-01-01 10:00:00', '2099-01-01 11:00:00', 'Confirmed');");
                await db.Database.MigrateAsync();
            }

            await using var read = new ParkingDbContext(new DbContextOptionsBuilder<ParkingDbContext>()
                .UseSqlite($"Data Source={path}").Options);
            var states = await read.ParkingSpots.OrderBy(s => s.Id).Select(s => s.State).ToListAsync();
            Assert.Equal(new[] { ParkingSpotState.OutOfService, ParkingSpotState.Free, ParkingSpotState.Occupied, ParkingSpotState.Free }, states);
            Assert.NotNull((await read.Reservations.SingleAsync(r => r.Id == 1)).CheckedInAt);
            Assert.Null((await read.Reservations.SingleAsync(r => r.Id == 2)).CheckedInAt);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
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
