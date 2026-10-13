using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartBus.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint4_TicketValidations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TicketValidations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    TripId = table.Column<Guid>(type: "uuid", nullable: true),
                    ValidatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardingStopId = table.Column<Guid>(type: "uuid", nullable: true),
                    ScannedCode = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClientScanId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TicketValidations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TicketValidations_Stops_BoardingStopId",
                        column: x => x.BoardingStopId,
                        principalTable: "Stops",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketValidations_Tickets_TicketId",
                        column: x => x.TicketId,
                        principalTable: "Tickets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketValidations_Trips_TripId",
                        column: x => x.TripId,
                        principalTable: "Trips",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TicketValidations_Users_ValidatedByUserId",
                        column: x => x.ValidatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TicketValidations_BoardingStopId",
                table: "TicketValidations",
                column: "BoardingStopId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketValidations_ClientScanId",
                table: "TicketValidations",
                column: "ClientScanId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketValidations_TicketId",
                table: "TicketValidations",
                column: "TicketId");

            migrationBuilder.CreateIndex(
                name: "IX_TicketValidations_TripId_ScannedAt",
                table: "TicketValidations",
                columns: new[] { "TripId", "ScannedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TicketValidations_ValidatedByUserId",
                table: "TicketValidations",
                column: "ValidatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TicketValidations");
        }
    }
}
