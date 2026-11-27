using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingReservation.Api.Migrations
{
    /// <inheritdoc />
    public partial class SpotStateSingleSourceAndCheckIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CheckedInAt",
                table: "Reservations",
                type: "TEXT",
                nullable: true);

            // BR-06: IsActive and OUT_OF_SERVICE are merged into one source of truth (State).
            migrationBuilder.Sql("UPDATE \"ParkingSpots\" SET \"State\" = 'OutOfService' WHERE \"IsActive\" = 0;");

            // BR-07: occupancy is bound to a reservation. An existing check-in (spot Occupied
            // + its CONFIRMED reservation that has already started) gets CheckedInAt = StartTime.
            migrationBuilder.Sql(@"UPDATE ""Reservations"" SET ""CheckedInAt"" = ""StartTime""
WHERE ""State"" = 'Confirmed'
  AND ""StartTime"" <= strftime('%Y-%m-%d %H:%M:%S', 'now')
  AND ""SpotId"" IN (SELECT ""Id"" FROM ""ParkingSpots"" WHERE ""State"" = 'Occupied');");

            // Occupied without a valid check-in (early check-in of an older version) could
            // never be released by check-out -> reset to Free.
            migrationBuilder.Sql(@"UPDATE ""ParkingSpots"" SET ""State"" = 'Free'
WHERE ""State"" = 'Occupied'
  AND ""Id"" NOT IN (SELECT ""SpotId"" FROM ""Reservations"" WHERE ""CheckedInAt"" IS NOT NULL);");

            // Reserved is no longer stored (it is derived from reservations).
            migrationBuilder.Sql("UPDATE \"ParkingSpots\" SET \"State\" = 'Free' WHERE \"State\" = 'Reserved';");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "ParkingSpots");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CheckedInAt",
                table: "Reservations");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "ParkingSpots",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.Sql("UPDATE \"ParkingSpots\" SET \"IsActive\" = 0 WHERE \"State\" = 'OutOfService';");
        }
    }
}
