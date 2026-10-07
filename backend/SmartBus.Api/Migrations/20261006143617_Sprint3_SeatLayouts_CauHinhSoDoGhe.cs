using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartBus.Api.Migrations
{
    /// <inheritdoc />
    public partial class Sprint3_SeatLayouts_CauHinhSoDoGhe : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ColumnsPerRow",
                table: "SeatLayouts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RowsPerFloor",
                table: "SeatLayouts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "VipSeatPositions",
                table: "SeatLayouts",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ColumnsPerRow",
                table: "SeatLayouts");

            migrationBuilder.DropColumn(
                name: "RowsPerFloor",
                table: "SeatLayouts");

            migrationBuilder.DropColumn(
                name: "VipSeatPositions",
                table: "SeatLayouts");
        }
    }
}
