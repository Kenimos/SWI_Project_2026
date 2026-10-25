using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ParkingReservation.Api.Contracts;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Tests;

public class ApiEndpointTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"parking-api-test-{Guid.NewGuid():N}.db");
    private readonly ApiFactory _factory;
    private HttpClient _client = null!;
    private int _areaId;
    private int _spotId;

    public ApiEndpointTests()
    {
        _factory = new ApiFactory(_dbPath);
    }

    public async Task InitializeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ParkingDbContext>();
        await db.Database.MigrateAsync();
        db.Users.AddRange(
            new User { Id = 1, Role = UserRole.Driver },
            new User { Id = 9, Role = UserRole.Approver });
        var area = new ParkingArea { Name = "API test area" };
        db.ParkingAreas.Add(area);
        await db.SaveChangesAsync();
        var spot = new ParkingSpot { AreaId = area.Id };
        db.ParkingSpots.Add(spot);
        await db.SaveChangesAsync();
        _areaId = area.Id;
        _spotId = spot.Id;
        _client = _factory.CreateClient();
        _client.BaseAddress = new Uri("https://localhost");
    }

    [Fact]
    public async Task Create_reservation_endpoint_returns_created_draft()
    {
        var response = await _client.PostAsJsonAsync("/reservations", new
        {
            userId = 1,
            spotId = _spotId,
            start = "2030-01-01T10:00:00Z",
            end = "2030-01-01T11:00:00Z"
        });

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("Draft", body.GetProperty("state").GetString());
        Assert.NotNull(response.Headers.Location);
    }

    [Fact]
    public async Task Confirm_endpoint_rejects_non_owner()
    {
        var reservation = new Reservation
        {
            SpotId = _spotId,
            UserId = 1,
            StartTime = new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc),
            EndTime = new DateTime(2030, 1, 1, 11, 0, 0, DateTimeKind.Utc)
        };
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ParkingDbContext>();
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync();
        }

        var response = await _client.PostAsJsonAsync($"/reservations/{reservation.Id}/confirm", new { userId = 9 });
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Area_availability_endpoint_returns_spot_state()
    {
        var response = await _client.GetAsync($"/parking-areas/{_areaId}/availability?start=2030-01-01T10:00:00Z&end=2030-01-01T11:00:00Z");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(_spotId, body[0].GetProperty("spotId").GetInt32());
        Assert.True(body[0].GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task Approver_can_create_area_endpoint()
    {
        var response = await _client.PostAsJsonAsync("/parking-areas", new
        {
            approverUserId = 9,
            name = "Second API area",
            address = "2 Main Street",
            type = (int)ParkingAreaType.Garage,
            openingHours = "24/7",
            pricingPolicy = "hourly"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        _factory.Dispose();
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }

        await Task.CompletedTask;
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dbPath;

    public ApiFactory(string dbPath)
    {
        _dbPath = dbPath;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", $"Data Source={_dbPath}");
    }
}
