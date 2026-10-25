using Microsoft.EntityFrameworkCore;
using ParkingReservation.Api.Domain;

namespace ParkingReservation.Api.Data;

public class ParkingDbContext : DbContext
{
    public ParkingDbContext(DbContextOptions<ParkingDbContext> options) : base(options)
    {
    }

    public DbSet<Reservation> Reservations => Set<Reservation>();

    public DbSet<ParkingArea> ParkingAreas => Set<ParkingArea>();

    public DbSet<User> Users => Set<User>();

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

            entity.HasOne(r => r.User)
                .WithMany()
                .HasForeignKey(r => r.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        });

          modelBuilder.Entity<ParkingArea>(entity =>
          {
            entity.Property(a => a.Type)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.Property(a => a.Name)
                .HasMaxLength(200)
                .IsRequired();
          });

          modelBuilder.Entity<User>(entity =>
          {
            entity.Property(u => u.Role)
                .HasConversion<string>()
                .HasMaxLength(20);
          });

        modelBuilder.Entity<ParkingSpot>(entity =>
        {
            entity.Property(s => s.Type)
                  .HasConversion<string>()
                  .HasMaxLength(20);

            entity.Property(s => s.State)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasOne(s => s.Area)
                .WithMany(a => a.Spots)
                .HasForeignKey(s => s.AreaId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
