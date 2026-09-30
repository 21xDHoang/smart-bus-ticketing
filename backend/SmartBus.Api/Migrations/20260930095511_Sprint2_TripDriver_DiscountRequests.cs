using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartBus.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_TripDriver_DiscountRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DriverId",
                table: "Trips",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DiscountRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PassengerType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EvidenceUrl = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RejectReason = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountRequests_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DiscountRequests_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DriverId_DepartureTime",
                table: "Trips",
                columns: new[] { "DriverId", "DepartureTime" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRequests_ReviewedByUserId",
                table: "DiscountRequests",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRequests_Status_CreatedAt",
                table: "DiscountRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRequests_Status_ExpiresAt",
                table: "DiscountRequests",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRequests_UserId",
                table: "DiscountRequests",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Trips_Users_DriverId",
                table: "Trips",
                column: "DriverId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Trips_Users_DriverId",
                table: "Trips");

            migrationBuilder.DropTable(
                name: "DiscountRequests");

            migrationBuilder.DropIndex(
                name: "IX_Trips_DriverId_DepartureTime",
                table: "Trips");

            migrationBuilder.DropColumn(
                name: "DriverId",
                table: "Trips");
        }
    }
}
