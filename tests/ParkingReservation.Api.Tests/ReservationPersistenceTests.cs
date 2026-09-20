using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Data;
using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Tests;

/// <summary>
/// C01 engineering spike A (Persistence): Reservation -> skutečná SQLite DB -> znovu načíst -> ověřit.
/// Každý test používá vlastní dočasný soubor .db a schéma vytvoří přes skutečné EF migrace.
/// </summary>
public class ReservationPersistenceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"parking-test-{Guid.NewGuid():N}.db");

    private ParkingDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new ParkingDbContext(options);
    }

    private ParkingDbContext CreateMigratedContext()
    {
        var context = CreateContext();
        context.Database.Migrate();
        return context;
    }

    [Fact]
    public void Reservation_is_saved_and_loaded_back_with_all_values()
    {
        var start = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);
        int savedId;

        // 1) uložit v jednom kontextu
        using (var writeContext = CreateMigratedContext())
        {
            var reservation = new Reservation
            {
                SpotId = 42,
                UserId = 7,
                StartTime = start,
                EndTime = end,
                State = ReservationState.Confirmed
            };
            writeContext.Reservations.Add(reservation);
            writeContext.SaveChanges();
            savedId = reservation.Id;
        }

        // 2) načíst v novém kontextu (nic se nebere z paměti, jen z DB souboru)
        using var readContext = CreateContext();
        var loaded = readContext.Reservations.Single(r => r.Id == savedId);

        Assert.True(savedId > 0);
        Assert.Equal(42, loaded.SpotId);
        Assert.Equal(7, loaded.UserId);
        Assert.Equal(start, loaded.StartTime);
        Assert.Equal(end, loaded.EndTime);
        Assert.Equal(ReservationState.Confirmed, loaded.State);
    }

    [Fact]
    public void Reservation_state_change_is_persisted()
    {
        int savedId;

        using (var writeContext = CreateMigratedContext())
        {
            var reservation = new Reservation
            {
                SpotId = 1,
                UserId = 1,
                StartTime = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc),
                EndTime = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc)
            };
            writeContext.Reservations.Add(reservation);
            writeContext.SaveChanges();
            savedId = reservation.Id;

            Assert.Equal(ReservationState.Draft, reservation.State);

            reservation.State = ReservationState.Confirmed; // DRAFT -> CONFIRMED
            writeContext.SaveChanges();
        }

        using var readContext = CreateContext();
        Assert.Equal(ReservationState.Confirmed, readContext.Reservations.Single(r => r.Id == savedId).State);
    }

    public void Dispose()
    {
        // SQLite si drží spojení v poolu, bez tohohle by šel soubor smazat jen někdy.
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath))
        {
            File.Delete(_dbPath);
        }
    }
}
