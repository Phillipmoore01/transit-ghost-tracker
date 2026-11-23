using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleStopProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "current_status",
                table: "vehicle_positions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "current_stop_sequence",
                table: "vehicle_positions",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "current_status",
                table: "vehicle_positions");

            migrationBuilder.DropColumn(
                name: "current_stop_sequence",
                table: "vehicle_positions");
        }
    }
}
