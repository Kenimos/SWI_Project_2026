using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParkingReservation.Api.Data;

public class ParkingDbContextFactory : IDesignTimeDbContextFactory<ParkingDbContext>
{
    public ParkingDbContext CreateDbContext(string[] args)
    {
        // Stejná proměnná jako za běhu aplikace, aby `dotnet ef database update`
        // šlo nasměrovat i na jinou DB bez --connection; výchozí je parking.db.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Data Source=parking.db";
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new ParkingDbContext(options);
    }
}
