using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ParkingReservation.Api.Data;

public class ParkingDbContextFactory : IDesignTimeDbContextFactory<ParkingDbContext>
{
    public ParkingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ParkingDbContext>()
            .UseSqlite("Data Source=parking.db")
            .Options;

        return new ParkingDbContext(options);
    }
}
