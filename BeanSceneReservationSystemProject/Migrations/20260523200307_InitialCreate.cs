using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace BeanSceneReservationSystemProject.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Areas",
                columns: table => new
                {
                    AreaId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AreaName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Areas", x => x.AreaId);
                });

            migrationBuilder.CreateTable(
                name: "Sittings",
                columns: table => new
                {
                    SittingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SittingType = table.Column<int>(type: "int", nullable: false),
                    StartDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndDateTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsClosed = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sittings", x => x.SittingId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FirstName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "RestaurantTables",
                columns: table => new
                {
                    RestaurantTableId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TableCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    IsAvailable = table.Column<bool>(type: "bit", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestaurantTables", x => x.RestaurantTableId);
                    table.ForeignKey(
                        name: "FK_RestaurantTables_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "AreaId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Members",
                columns: table => new
                {
                    MemberId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    JoinDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Members", x => x.MemberId);
                    table.ForeignKey(
                        name: "FK_Members_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    ReservationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GuestFirstName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    GuestLastName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    SittingId = table.Column<int>(type: "int", nullable: false),
                    MemberId = table.Column<int>(type: "int", nullable: true),
                    StartTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    NumberOfGuests = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.ReservationId);
                    table.ForeignKey(
                        name: "FK_Reservations_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "MemberId");
                    table.ForeignKey(
                        name: "FK_Reservations_Sittings_SittingId",
                        column: x => x.SittingId,
                        principalTable: "Sittings",
                        principalColumn: "SittingId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReservationStatusHistories",
                columns: table => new
                {
                    ReservationStatusHistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    OldStatus = table.Column<int>(type: "int", nullable: true),
                    NewStatus = table.Column<int>(type: "int", nullable: false),
                    ChangedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ChangedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationStatusHistories", x => x.ReservationStatusHistoryId);
                    table.ForeignKey(
                        name: "FK_ReservationStatusHistories_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "ReservationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReservationStatusHistories_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "ReservationTables",
                columns: table => new
                {
                    ReservationTableId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReservationId = table.Column<int>(type: "int", nullable: false),
                    RestaurantTableId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReservationTables", x => x.ReservationTableId);
                    table.ForeignKey(
                        name: "FK_ReservationTables_Reservations_ReservationId",
                        column: x => x.ReservationId,
                        principalTable: "Reservations",
                        principalColumn: "ReservationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReservationTables_RestaurantTables_RestaurantTableId",
                        column: x => x.RestaurantTableId,
                        principalTable: "RestaurantTables",
                        principalColumn: "RestaurantTableId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Areas",
                columns: new[] { "AreaId", "AreaName" },
                values: new object[,]
                {
                    { 1, "Main" },
                    { 2, "Outside" },
                    { 3, "Balcony" }
                });

            migrationBuilder.InsertData(
                table: "Sittings",
                columns: new[] { "SittingId", "Capacity", "EndDateTime", "IsClosed", "SittingType", "StartDateTime" },
                values: new object[,]
                {
                    { 1, 40, new DateTime(2026, 6, 1, 11, 0, 0, 0, DateTimeKind.Unspecified), false, 0, new DateTime(2026, 6, 1, 7, 0, 0, 0, DateTimeKind.Unspecified) },
                    { 2, 50, new DateTime(2026, 6, 1, 15, 0, 0, 0, DateTimeKind.Unspecified), false, 1, new DateTime(2026, 6, 1, 12, 0, 0, 0, DateTimeKind.Unspecified) },
                    { 3, 60, new DateTime(2026, 6, 1, 23, 0, 0, 0, DateTimeKind.Unspecified), false, 2, new DateTime(2026, 6, 1, 18, 0, 0, 0, DateTimeKind.Unspecified) }
                });

            migrationBuilder.InsertData(
                table: "RestaurantTables",
                columns: new[] { "RestaurantTableId", "AreaId", "Capacity", "IsAvailable", "TableCode" },
                values: new object[,]
                {
                    { 1, 1, 4, true, "M1" },
                    { 2, 1, 4, true, "M2" },
                    { 3, 1, 4, true, "M3" },
                    { 4, 1, 4, true, "M4" },
                    { 5, 1, 4, true, "M5" },
                    { 6, 1, 4, true, "M6" },
                    { 7, 1, 4, true, "M7" },
                    { 8, 1, 4, true, "M8" },
                    { 9, 1, 4, true, "M9" },
                    { 10, 1, 4, true, "M10" },
                    { 11, 2, 4, true, "O1" },
                    { 12, 2, 4, true, "O2" },
                    { 13, 2, 4, true, "O3" },
                    { 14, 2, 4, true, "O4" },
                    { 15, 2, 4, true, "O5" },
                    { 16, 2, 4, true, "O6" },
                    { 17, 2, 4, true, "O7" },
                    { 18, 2, 4, true, "O8" },
                    { 19, 2, 4, true, "O9" },
                    { 20, 2, 4, true, "O10" },
                    { 21, 3, 4, true, "B1" },
                    { 22, 3, 4, true, "B2" },
                    { 23, 3, 4, true, "B3" },
                    { 24, 3, 4, true, "B4" },
                    { 25, 3, 4, true, "B5" },
                    { 26, 3, 4, true, "B6" },
                    { 27, 3, 4, true, "B7" },
                    { 28, 3, 4, true, "B8" },
                    { 29, 3, 4, true, "B9" },
                    { 30, 3, 4, true, "B10" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Members_UserId",
                table: "Members",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_MemberId",
                table: "Reservations",
                column: "MemberId");

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_SittingId",
                table: "Reservations",
                column: "SittingId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationStatusHistories_ChangedByUserId",
                table: "ReservationStatusHistories",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationStatusHistories_ReservationId",
                table: "ReservationStatusHistories",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationTables_ReservationId",
                table: "ReservationTables",
                column: "ReservationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReservationTables_RestaurantTableId",
                table: "ReservationTables",
                column: "RestaurantTableId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTables_AreaId",
                table: "RestaurantTables",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_RestaurantTables_TableCode",
                table: "RestaurantTables",
                column: "TableCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReservationStatusHistories");

            migrationBuilder.DropTable(
                name: "ReservationTables");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "RestaurantTables");

            migrationBuilder.DropTable(
                name: "Members");

            migrationBuilder.DropTable(
                name: "Sittings");

            migrationBuilder.DropTable(
                name: "Areas");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
