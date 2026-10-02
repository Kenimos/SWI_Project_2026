using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Data;

public class ParkingDbContext : DbContext
{
    public ParkingDbContext(DbContextOptions<ParkingDbContext> options) : base(options)
    {
    }

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<ParkingSpot> ParkingSpots => Set<ParkingSpot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Reservation>(entity =>
        {
            // Stav se v DB ukládá jako text ("Confirmed"), ne jako číslo – v DB je pak čitelný.
            entity.Property(r => r.State)
                  .HasConversion<string>()
                  .HasMaxLength(20);

            entity.HasOne(r => r.Spot)
                  .WithMany()
                  .HasForeignKey(r => r.SpotId)
                  .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ParkingSpot>(entity =>
        {
            entity.Property(s => s.Type)
                  .HasConversion<string>()
                  .HasMaxLength(20);
        });
    }
}
