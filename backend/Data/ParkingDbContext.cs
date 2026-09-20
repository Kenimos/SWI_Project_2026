using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Data;

public class ParkingDbContext : DbContext
{
    public ParkingDbContext(DbContextOptions<ParkingDbContext> options) : base(options)
    {
    }

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reservation>(entity =>
        {
            // Stav se v DB ukládá jako text ("Confirmed"), ne jako číslo – v DB je pak čitelný.
            entity.Property(r => r.State)
                  .HasConversion<string>()
                  .HasMaxLength(20);
        });
    }
}
