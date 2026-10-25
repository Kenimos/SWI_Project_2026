using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;
using ParkingReservation.Api.Services;

namespace ParkingReservation.Api.Tests;

public class ParkingAreaAndSpotStateTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"parking-test-{Guid.NewGuid():N}.db");
    private readonly ParkingDbContext _db;
    private readonly FakeClock _clock;
    private readonly ReservationService _service;

    private static readonly DateTime Now = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public ParkingAreaAndSpotStateTests()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new ParkingDbContext(options);
        _db.Database.Migrate();
        _db.Users.AddRange(
            new User { Id = 1, Role = UserRole.Driver },
            new User { Id = 9, Role = UserRole.Approver });
        _db.SaveChanges();
        _clock = new FakeClock { UtcNow = Now };
        _service = new ReservationService(_db, _clock, TimeSpan.FromHours(24));
    }

    [Fact]
    public async Task Approver_can_create_area_and_spot()
    {
        var areaResult = await _service.CreateParkingAreaAsync(new CreateParkingAreaRequest(
            ApproverUserId: 9,
            Name: "Central garage",
            Address: "1 Main Street",
            Type: ParkingAreaType.Garage,
            OpeningHours: "24/7",
            PricingPolicy: "hourly"));

        Assert.True(areaResult.Success);

        var spotResult = await _service.CreateParkingSpotAsync(
            areaResult.AreaId!.Value,
            new CreateParkingSpotRequest(9, ParkingSpotType.Standard));

        Assert.True(spotResult.Success);
        Assert.Equal(ParkingSpotState.Free, (await _db.ParkingSpots.FindAsync(spotResult.SpotId))!.State);
    }

    [Fact]
    public async Task Driver_cannot_manage_area_or_spot_state()
    {
        var areaResult = await _service.CreateParkingAreaAsync(new CreateParkingAreaRequest(
            1, "Driver area", null, ParkingAreaType.OpenLot, null, null));

        Assert.False(areaResult.Success);
        Assert.Equal(ParkingAreaError.Unauthorized, areaResult.Error);
    }

    [Fact]
    public async Task Approver_cannot_create_reservation()
    {
        var area = new ParkingArea { Name = "Test area" };
        _db.ParkingAreas.Add(area);
        await _db.SaveChangesAsync();
        var spot = new ParkingSpot { AreaId = area.Id };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();

        var result = await _service.CreateReservationAsync(new CreateReservationRequest(
            9, spot.Id, Now.AddHours(1), Now.AddHours(2)));

        Assert.False(result.Success);
        Assert.Equal(CreateReservationError.Unauthorized, result.Error);
    }

    [Fact]
    public async Task Only_reservation_owner_can_confirm_or_cancel()
    {
        var area = new ParkingArea { Name = "Test area" };
        _db.ParkingAreas.Add(area);
        await _db.SaveChangesAsync();
        var spot = new ParkingSpot { AreaId = area.Id };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();

        var created = await _service.CreateReservationAsync(new CreateReservationRequest(
            1, spot.Id, Now.AddHours(1), Now.AddHours(2)));

        var confirm = await _service.ConfirmReservationAsync(created.ReservationId!.Value, 9);
        var cancel = await _service.CancelReservationAsync(created.ReservationId.Value, 9);

        Assert.False(confirm.Success);
        Assert.Equal(ConfirmReservationError.Unauthorized, confirm.Error);
        Assert.False(cancel.Success);
        Assert.Equal(CancelReservationError.Unauthorized, cancel.Error);
    }

    [Fact]
    public async Task Area_availability_reports_confirmed_and_out_of_service_spots()
    {
        var area = new ParkingArea { Name = "Test area" };
        _db.ParkingAreas.Add(area);
        await _db.SaveChangesAsync();
        var freeSpot = new ParkingSpot { AreaId = area.Id };
        var blockedSpot = new ParkingSpot { AreaId = area.Id, State = ParkingSpotState.OutOfService };
        _db.ParkingSpots.AddRange(freeSpot, blockedSpot);
        await _db.SaveChangesAsync();

        var result = await _service.CheckAreaAvailabilityAsync(area.Id, Now.AddHours(1), Now.AddHours(2));

        Assert.True(result.Success);
        Assert.Contains(result.Spots, item => item.SpotId == freeSpot.Id && item.Available);
        Assert.Contains(result.Spots, item => item.SpotId == blockedSpot.Id && !item.Available);
    }

    [Fact]
    public async Task Confirm_check_in_and_check_out_update_physical_spot_state()
    {
        var area = new ParkingArea { Name = "Test area" };
        _db.ParkingAreas.Add(area);
        await _db.SaveChangesAsync();
        var spot = new ParkingSpot { AreaId = area.Id };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();

        var created = await _service.CreateReservationAsync(new CreateReservationRequest(
            1, spot.Id, Now.AddHours(1), Now.AddHours(2)));
        var confirmed = await _service.ConfirmReservationAsync(created.ReservationId!.Value, 1);
        var checkedIn = await _service.CheckInAsync(created.ReservationId.Value, 9);
        var checkedOut = await _service.CheckOutAsync(created.ReservationId.Value, 9);

        Assert.True(confirmed.Success);
        Assert.Equal(ParkingSpotState.Occupied, checkedIn.State);
        Assert.Equal(ParkingSpotState.Free, checkedOut.State);
    }

    [Fact]
    public async Task Approver_can_set_free_spot_out_of_service_and_restore_it()
    {
        var area = new ParkingArea { Name = "Test area" };
        _db.ParkingAreas.Add(area);
        await _db.SaveChangesAsync();
        var spot = new ParkingSpot { AreaId = area.Id };
        _db.ParkingSpots.Add(spot);
        await _db.SaveChangesAsync();

        var outOfService = await _service.SetSpotOutOfServiceAsync(spot.Id, 9);
        var restored = await _service.RestoreSpotAsync(spot.Id, 9);

        Assert.Equal(ParkingSpotState.OutOfService, outOfService.State);
        Assert.Equal(ParkingSpotState.Free, restored.State);
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
