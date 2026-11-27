using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParkingReservation.Api.Data;

public class ParkingDbContextFactory : IDesignTimeDbContextFactory<ParkingDbContext>
{
    public ParkingDbContext CreateDbContext(string[] args)
    {
        // Same variable as at application run time, so `dotnet ef database update`
        // can target another DB without --connection; the default is parking.db.
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Data Source=parking.db";
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite(connectionString)
            .Options;

        return new ParkingDbContext(options);
    }
}
