using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Transit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStaticSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gtfs_feeds",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_url = table.Column<string>(type: "text", nullable: false),
                    feed_version = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    imported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_gtfs_feeds", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "agencies",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    agency_id = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "text", nullable: false),
                    time_zone = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agencies", x => x.id);
                    table.ForeignKey(
                        name: "fk_agencies_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "calendar_dates",
                columns: table => new
                {
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    exception_type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_calendar_dates", x => new { x.feed_id, x.service_id, x.date });
                    table.ForeignKey(
                        name: "fk_calendar_dates_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "routes",
                columns: table => new
                {
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    route_id = table.Column<string>(type: "text", nullable: false),
                    agency_id = table.Column<string>(type: "text", nullable: true),
                    short_name = table.Column<string>(type: "text", nullable: true),
                    long_name = table.Column<string>(type: "text", nullable: true),
                    route_type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_routes", x => new { x.feed_id, x.route_id });
                    table.ForeignKey(
                        name: "fk_routes_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "service_calendars",
                columns: table => new
                {
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    service_id = table.Column<string>(type: "text", nullable: false),
                    monday = table.Column<bool>(type: "boolean", nullable: false),
                    tuesday = table.Column<bool>(type: "boolean", nullable: false),
                    wednesday = table.Column<bool>(type: "boolean", nullable: false),
                    thursday = table.Column<bool>(type: "boolean", nullable: false),
                    friday = table.Column<bool>(type: "boolean", nullable: false),
                    saturday = table.Column<bool>(type: "boolean", nullable: false),
                    sunday = table.Column<bool>(type: "boolean", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_service_calendars", x => new { x.feed_id, x.service_id });
                    table.ForeignKey(
                        name: "fk_service_calendars_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stop_times",
                columns: table => new
                {
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    trip_id = table.Column<string>(type: "text", nullable: false),
                    stop_sequence = table.Column<int>(type: "integer", nullable: false),
                    stop_id = table.Column<string>(type: "text", nullable: false),
                    arrival_time = table.Column<TimeSpan>(type: "interval", nullable: true),
                    departure_time = table.Column<TimeSpan>(type: "interval", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stop_times", x => new { x.feed_id, x.trip_id, x.stop_sequence });
                    table.ForeignKey(
                        name: "fk_stop_times_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stops",
                columns: table => new
                {
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    stop_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    location_type = table.Column<int>(type: "integer", nullable: false),
                    parent_station = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stops", x => new { x.feed_id, x.stop_id });
                    table.ForeignKey(
                        name: "fk_stops_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trips",
                columns: table => new
                {
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    trip_id = table.Column<string>(type: "text", nullable: false),
                    route_id = table.Column<string>(type: "text", nullable: false),
                    service_id = table.Column<string>(type: "text", nullable: false),
                    direction_id = table.Column<int>(type: "integer", nullable: true),
                    headsign = table.Column<string>(type: "text", nullable: true),
                    block_id = table.Column<string>(type: "text", nullable: true),
                    shape_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trips", x => new { x.feed_id, x.trip_id });
                    table.ForeignKey(
                        name: "fk_trips_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agencies_feed_id_agency_id",
                table: "agencies",
                columns: new[] { "feed_id", "agency_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gtfs_feeds_sha256",
                table: "gtfs_feeds",
                column: "sha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stop_times_feed_id_stop_id",
                table: "stop_times",
                columns: new[] { "feed_id", "stop_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trips_feed_id_route_id",
                table: "trips",
                columns: new[] { "feed_id", "route_id" });

            migrationBuilder.CreateIndex(
                name: "ix_trips_feed_id_service_id",
                table: "trips",
                columns: new[] { "feed_id", "service_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agencies");

            migrationBuilder.DropTable(
                name: "calendar_dates");

            migrationBuilder.DropTable(
                name: "routes");

            migrationBuilder.DropTable(
                name: "service_calendars");

            migrationBuilder.DropTable(
                name: "stop_times");

            migrationBuilder.DropTable(
                name: "stops");

            migrationBuilder.DropTable(
                name: "trips");

            migrationBuilder.DropTable(
                name: "gtfs_feeds");
        }
    }
}
