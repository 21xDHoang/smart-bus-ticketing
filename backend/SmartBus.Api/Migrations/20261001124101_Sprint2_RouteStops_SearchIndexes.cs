using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartBus.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint2_RouteStops_SearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RouteStops_StopId",
                table: "RouteStops");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_RouteId_StopOrder",
                table: "RouteStops",
                columns: new[] { "RouteId", "StopOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_StopId_RouteId_StopOrder",
                table: "RouteStops",
                columns: new[] { "StopId", "RouteId", "StopOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RouteStops_RouteId_StopOrder",
                table: "RouteStops");

            migrationBuilder.DropIndex(
                name: "IX_RouteStops_StopId_RouteId_StopOrder",
                table: "RouteStops");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_StopId",
                table: "RouteStops",
                column: "StopId");
        }
    }
}
