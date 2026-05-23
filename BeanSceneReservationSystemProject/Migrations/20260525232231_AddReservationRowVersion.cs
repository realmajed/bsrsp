using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanSceneReservationSystemProject.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReservationTables_ReservationId",
                table: "ReservationTables");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Reservations",
                type: "rowversion",
                rowVersion: true,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReservationTables_ReservationId_RestaurantTableId",
                table: "ReservationTables",
                columns: new[] { "ReservationId", "RestaurantTableId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReservationTables_ReservationId_RestaurantTableId",
                table: "ReservationTables");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Reservations");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationTables_ReservationId",
                table: "ReservationTables",
                column: "ReservationId");
        }
    }
}
