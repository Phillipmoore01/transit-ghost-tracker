using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Transit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddObservedStopArrivals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "observed_stop_arrivals",
                columns: table => new
                {
                    trip_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    stop_sequence = table.Column<int>(type: "integer", nullable: false),
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    route_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    stop_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    vehicle_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    evidence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observed_stop_arrivals", x => new { x.trip_id, x.service_date, x.stop_sequence });
                    table.ForeignKey(
                        name: "fk_observed_stop_arrivals_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stop_arrival_runs",
                columns: table => new
                {
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trips_observed = table.Column<int>(type: "integer", nullable: false),
                    trips_matched = table.Column<int>(type: "integer", nullable: false),
                    arrivals_recorded = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stop_arrival_runs", x => x.service_date);
                });

            migrationBuilder.CreateIndex(
                name: "ix_vehicle_positions_start_date_trip_id",
                table: "vehicle_positions",
                columns: new[] { "start_date", "trip_id" });

            migrationBuilder.CreateIndex(
                name: "ix_observed_stop_arrivals_feed_id",
                table: "observed_stop_arrivals",
                column: "feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_observed_stop_arrivals_route_id_service_date",
                table: "observed_stop_arrivals",
                columns: new[] { "route_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "ix_observed_stop_arrivals_service_date",
                table: "observed_stop_arrivals",
                column: "service_date");

            migrationBuilder.CreateIndex(
                name: "ix_observed_stop_arrivals_stop_id_service_date",
                table: "observed_stop_arrivals",
                columns: new[] { "stop_id", "service_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "observed_stop_arrivals");

            migrationBuilder.DropTable(
                name: "stop_arrival_runs");

            migrationBuilder.DropIndex(
                name: "ix_vehicle_positions_start_date_trip_id",
                table: "vehicle_positions");
        }
    }
}
