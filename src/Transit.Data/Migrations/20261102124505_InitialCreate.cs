using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Transit.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vehicle_positions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    vehicle_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    trip_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    route_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    stop_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vehicle_positions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_positions_observed_at",
                table: "vehicle_positions",
                column: "observed_at");

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_positions_trip_id_start_date",
                table: "vehicle_positions",
                columns: new[] { "trip_id", "start_date" });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_positions_vehicle_id_observed_at",
                table: "vehicle_positions",
                columns: new[] { "vehicle_id", "observed_at" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vehicle_positions");
        }
    }
}
