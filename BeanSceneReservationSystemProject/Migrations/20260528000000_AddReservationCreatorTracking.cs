using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BeanSceneReservationSystemProject.Migrations
{
    public partial class AddReservationCreatorTracking : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Reservations",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_CreatedByUserId",
                table: "Reservations",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Reservations_AspNetUsers_CreatedByUserId",
                table: "Reservations",
                column: "CreatedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reservations_AspNetUsers_CreatedByUserId",
                table: "Reservations");

            migrationBuilder.DropIndex(
                name: "IX_Reservations_CreatedByUserId",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Reservations");
        }
    }
}
