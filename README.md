# transit-ghost-tracker

Measures how reliably a transit agency runs its published schedule: which scheduled trips never ran ("ghost buses") and how far actual arrivals drift from scheduled times, per route and per stop.

## What it does

- Polls an agency's GTFS-Realtime vehicle positions feed every 15–30 seconds, continuously.
- Matches each vehicle position to a scheduled trip from the agency's static GTFS schedule.
- Records trips that were scheduled but never observed, and actual vs. scheduled arrival times per stop.
- Serves a public page per route and a lookup per stop, e.g. "Route 42: 11% of weekday evening trips did not run last month."

## Data sources

- Static GTFS schedule (routes, trips, stop times, shapes) from the agency.
- GTFS-Realtime vehicle positions feed. Initial target: MBTA (public feed, no key). Other agencies such as MTA New York require a free API key.

## Engineering problems

- **Volume:** millions of position rows per month for a mid-size agency; requires time-based partitioning, retention policy, and pre-aggregated reporting tables.
- **Trip matching:** GPS jitter, duplicate pings, vehicles dropping in and out of the feed, trips crossing midnight, and service-day boundaries.
- **Ingestion reliability:** idempotent writes, gap detection when the poller is down, and alerting.
- **Continuous operation:** containerized deployment to a cloud provider with monitoring.

## Correctness checks

- Replay tests: recorded feed snapshots run through the matcher with known expected outcomes.
- Aggregate on-time figures compared against the agency's own published performance reports.
- Individual ghost trips verifiable by route, trip ID, and service date.

## Planned stack

- .NET 10 worker service (ingestion) and ASP.NET Core API
- PostgreSQL with time-based partitioning
- Docker, deployed to AWS or Azure
- xUnit and Testcontainers
- GitHub Actions CI

## Repository layout

- `src/Transit.Gtfs` — GTFS-Realtime protobuf bindings (generated from `Protos/gtfs-realtime.proto`), vehicle position extraction, GTFS service-day time resolution, the static GTFS zip reader (`Static/`), and stop arrival inference (`Arrivals/`).
- `src/Transit.Data` — EF Core model, migrations, idempotent vehicle position writer, bulk static schedule importer, and per-service-date stop arrival recorder for PostgreSQL.
- `src/Transit.Ingestion` — long-running worker that polls the vehicle positions feed, refreshes the static schedule, and records stop arrivals for completed service days.
- `src/Transit.Web` — ASP.NET Core Razor Pages site for route and stop reports.
- `tests/Transit.Gtfs.Tests` — time resolution, feed extraction, and stop arrival inference tests.
- `tests/Transit.Ingestion.Tests` — feed client and service-day completion tests.
- `tests/Transit.Data.Tests` — PostgreSQL integration tests (Testcontainers; require Docker).

## Getting started

Requires the .NET 10 SDK and Docker (for local PostgreSQL).

```
docker compose up -d
dotnet build
dotnet test
dotnet tool restore
dotnet ef database update --project src/Transit.Data --startup-project src/Transit.Web
dotnet run --project src/Transit.Ingestion
```

Schema changes are made with `dotnet ef migrations add <Name> --project src/Transit.Data --startup-project src/Transit.Web`. Database identifiers use snake_case.

The feed URL and poll interval are set under `GtfsRealtime` in `src/Transit.Ingestion/appsettings.json` (default: MBTA vehicle positions every 20 seconds). The static schedule URL and refresh interval are set under `GtfsStatic` (default: MBTA every 6 hours); each download is hashed and imported as a new feed only when its contents have changed, and earlier feeds are kept.

Stop arrivals are recorded once per completed service date under `StopArrivals` (default: checked every 30 minutes, looking back 3 days). A service date counts as complete 30 hours after its start in the agency time zone, so trips scheduled past midnight are included. Each observed trip is matched by trip ID to the newest static schedule imported before the trip was first seen. A stop's arrival is the first report of the vehicle stopped there; when polling missed the stop, the arrival is interpolated along the scheduled times between the reports before and after it, provided they are at most 3 minutes apart. Each row in `observed_stop_arrivals` records which method was used (`evidence`). The first stop of a trip is not recorded, and positions without a trip start date are not used. Rerunning a service date replaces its rows; `stop_arrival_runs` records the counts for each run.

The local database accepts connections from `127.0.0.1` without a password; do not reuse `docker-compose.yml` outside local development.

## Milestones

- [x] Load static GTFS for one agency into PostgreSQL
- [x] Poll and persist vehicle positions with idempotent writes
- [x] Match positions to scheduled trips; compute per-stop arrival deltas
- [ ] Detect trips that never ran for a completed service day
- [ ] Containerize and deploy; add gap detection and alerts
- [ ] Public route and stop pages
- [ ] Compare results against published agency performance data
