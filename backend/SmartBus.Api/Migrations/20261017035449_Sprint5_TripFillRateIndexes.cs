using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartBus.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint5_TripFillRateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_RouteId_DepartureTime",
                table: "Trips");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_DepartureTime",
                table: "Trips",
                column: "DepartureTime")
                .Annotation("Npgsql:IndexInclude", new[] { "Id", "RouteId", "BusId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Trips_RouteId_DepartureTime",
                table: "Trips",
                columns: new[] { "RouteId", "DepartureTime" })
                .Annotation("Npgsql:IndexInclude", new[] { "Id", "BusId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tickets_TripId_Status",
                table: "Tickets",
                columns: new[] { "TripId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Trips_DepartureTime",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Trips_RouteId_DepartureTime",
                table: "Trips");

            migrationBuilder.DropIndex(
                name: "IX_Tickets_TripId_Status",
                table: "Tickets");

            migrationBuilder.CreateIndex(
                name: "IX_Trips_RouteId_DepartureTime",
                table: "Trips",
                columns: new[] { "RouteId", "DepartureTime" });
        }
    }
}
