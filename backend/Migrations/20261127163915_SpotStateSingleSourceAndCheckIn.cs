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

            // BR-06: IsActive a OUT_OF_SERVICE se slučují do jednoho zdroje pravdy (State).
            migrationBuilder.Sql("UPDATE \"ParkingSpots\" SET \"State\" = 'OutOfService' WHERE \"IsActive\" = 0;");

            // BR-07: obsazení se váže na rezervaci. Stávající check-in (místo Occupied
            // + jeho CONFIRMED rezervace, která už začala) dostane CheckedInAt = StartTime.
            migrationBuilder.Sql(@"UPDATE ""Reservations"" SET ""CheckedInAt"" = ""StartTime""
WHERE ""State"" = 'Confirmed'
  AND ""StartTime"" <= strftime('%Y-%m-%d %H:%M:%S', 'now')
  AND ""SpotId"" IN (SELECT ""Id"" FROM ""ParkingSpots"" WHERE ""State"" = 'Occupied');");

            // Occupied bez platného check-inu (předčasný check-in starší verze) by nešlo
            // nikdy uvolnit check-outem -> vrátit na Free.
            migrationBuilder.Sql(@"UPDATE ""ParkingSpots"" SET ""State"" = 'Free'
WHERE ""State"" = 'Occupied'
  AND ""Id"" NOT IN (SELECT ""SpotId"" FROM ""Reservations"" WHERE ""CheckedInAt"" IS NOT NULL);");

            // Reserved se už neukládá (odvozuje se z rezervací).
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
