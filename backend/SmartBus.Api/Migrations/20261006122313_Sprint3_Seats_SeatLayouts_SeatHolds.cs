using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartBus.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3_Seats_SeatLayouts_SeatHolds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ColumnIndex",
                table: "Seats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Floor",
                table: "Seats",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "RowIndex",
                table: "Seats",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "SeatLayoutId",
                table: "Seats",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "SeatType",
                table: "Seats",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Standard");

            migrationBuilder.CreateTable(
                name: "SeatHolds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TripId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeatId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SeatHolds_Seats_SeatId",
                        column: x => x.SeatId,
                        principalTable: "Seats",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeatHolds_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SeatHolds_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SeatLayouts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    NumberOfFloors = table.Column<int>(type: "integer", nullable: false),
                    TotalSeats = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeatLayouts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Seats_SeatLayoutId",
                table: "Seats",
                column: "SeatLayoutId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatHolds_ExpiresAt",
                table: "SeatHolds",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_SeatHolds_SeatId",
                table: "SeatHolds",
                column: "SeatId");

            migrationBuilder.CreateIndex(
                name: "IX_SeatHolds_SessionCode",
                table: "SeatHolds",
                column: "SessionCode");

            migrationBuilder.CreateIndex(
                name: "IX_SeatHolds_TripId_SeatId",
                table: "SeatHolds",
                columns: new[] { "TripId", "SeatId" },
                unique: true,
                filter: "\"Status\" = 'Holding'");

            migrationBuilder.CreateIndex(
                name: "IX_SeatHolds_UserId_CreatedAt",
                table: "SeatHolds",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SeatLayouts_BusType",
                table: "SeatLayouts",
                column: "BusType",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Seats_SeatLayouts_SeatLayoutId",
                table: "Seats",
                column: "SeatLayoutId",
                principalTable: "SeatLayouts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Seats_SeatLayouts_SeatLayoutId",
                table: "Seats");

            migrationBuilder.DropTable(
                name: "SeatHolds");

            migrationBuilder.DropTable(
                name: "SeatLayouts");

            migrationBuilder.DropIndex(
                name: "IX_Seats_SeatLayoutId",
                table: "Seats");

            migrationBuilder.DropColumn(
                name: "ColumnIndex",
                table: "Seats");

            migrationBuilder.DropColumn(
                name: "Floor",
                table: "Seats");

            migrationBuilder.DropColumn(
                name: "RowIndex",
                table: "Seats");

            migrationBuilder.DropColumn(
                name: "SeatLayoutId",
                table: "Seats");

            migrationBuilder.DropColumn(
                name: "SeatType",
                table: "Seats");
        }
    }
}
