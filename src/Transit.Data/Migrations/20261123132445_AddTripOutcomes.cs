using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Transit.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTripOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "feed_polls",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    feed_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    entity_count = table.Column<int>(type: "integer", nullable: false),
                    positions_inserted = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_feed_polls", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "scheduled_trip_outcomes",
                columns: table => new
                {
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    trip_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    route_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    direction_id = table.Column<int>(type: "integer", nullable: true),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    scheduled_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: false),
                    first_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduled_trip_outcomes", x => new { x.service_date, x.trip_id });
                    table.ForeignKey(
                        name: "fk_scheduled_trip_outcomes_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trip_outcome_runs",
                columns: table => new
                {
                    service_date = table.Column<DateOnly>(type: "date", nullable: false),
                    feed_id = table.Column<int>(type: "integer", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trips_scheduled = table.Column<int>(type: "integer", nullable: false),
                    trips_observed = table.Column<int>(type: "integer", nullable: false),
                    trips_not_observed = table.Column<int>(type: "integer", nullable: false),
                    trips_in_feed_gaps = table.Column<int>(type: "integer", nullable: false),
                    trips_on_untracked_routes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_trip_outcome_runs", x => x.service_date);
                    table.ForeignKey(
                        name: "fk_trip_outcome_runs_gtfs_feeds_feed_id",
                        column: x => x.feed_id,
                        principalTable: "gtfs_feeds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_feed_polls_feed_timestamp",
                table: "feed_polls",
                column: "feed_timestamp");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_trip_outcomes_feed_id",
                table: "scheduled_trip_outcomes",
                column: "feed_id");

            migrationBuilder.CreateIndex(
                name: "ix_scheduled_trip_outcomes_route_id_service_date",
                table: "scheduled_trip_outcomes",
                columns: new[] { "route_id", "service_date" });

            migrationBuilder.CreateIndex(
                name: "ix_trip_outcome_runs_feed_id",
                table: "trip_outcome_runs",
                column: "feed_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "feed_polls");

            migrationBuilder.DropTable(
                name: "scheduled_trip_outcomes");

            migrationBuilder.DropTable(
                name: "trip_outcome_runs");
        }
    }
}
