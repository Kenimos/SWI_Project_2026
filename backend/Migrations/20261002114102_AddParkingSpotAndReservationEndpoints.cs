using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ParkingReservation.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddParkingSpotAndReservationEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ParkingSpots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ParkingSpots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_SpotId",
                table: "Reservations",
                column: "SpotId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_ParkingSpots_SpotId",
                table: "Reservations",
                column: "SpotId",
                principalTable: "ParkingSpots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_ParkingSpots_SpotId",
                table: "Reservations");

            migrationBuilder.DropTable(
                name: "ParkingSpots");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_SpotId",
                table: "Reservations");
        }
    }
}
