# transit-ghost-tracker

Measures how reliably a transit agency runs its published schedule: which scheduled trips never ran ("ghost buses") and how far actual arrivals drift from scheduled times, per route and per stop.

## Status

- Ingestion, arrival inference, missing-trip detection, outage detection, and the public report pages are implemented and tested.
- The full stack runs in containers (`compose.deploy.yml`); it is not yet deployed to a permanently running host.
- Initial target agency: MBTA (public feeds, no API key).

## How it works

1. **Static schedule.** Every 6 hours the agency's GTFS zip is downloaded and hashed. A changed file is bulk-imported as a new feed; earlier feeds are kept so past service days are judged against the schedule that was in effect.
2. **Vehicle positions.** Every 20 seconds the GTFS-Realtime vehicle positions feed is polled. Positions are stored with idempotent batch inserts, including the vehicle's current stop sequence and stop status. Each successful poll is logged in `feed_polls` with the feed's own timestamp.
3. **Stop arrivals.** For each completed service day, every observed trip is matched by trip ID to the newest schedule imported before the trip was first seen. A stop's arrival is the first report of the vehicle stopped there. When polling missed the stop, the arrival is interpolated along the scheduled times between the reports before and after it, provided they are at most 3 minutes apart. Results go to `observed_stop_arrivals`, with the method used (`evidence`) and the scheduled time resolved in the agency time zone.
4. **Trip outcomes.** For each completed service day, every trip whose service is active that date (`calendar.txt` plus `calendar_dates.txt` exceptions) in the schedule in effect at the start of the day gets one row in `scheduled_trip_outcomes`:
   - `Observed` — at least one vehicle position reported the trip.
   - `NotObserved` — never reported, other trips on its route were observed that day, and the feed had no gap longer than 2 minutes during its scheduled time. Only these count as trips that did not run.
   - `FeedGap` — never reported, but polling or the feed was interrupted during its scheduled time.
   - `RouteNotTracked` — no trip on the route was observed that day, which usually means the route is not in the realtime feed.
5. **Outages.** Every 30 seconds the worker looks for periods longer than 2 minutes without a new feed timestamp and records them in `feed_outages`, including gaps found after a restart. Each outage is logged when it starts and ends, and optionally posted to a webhook.

A service day counts as complete 30 hours after it starts in the agency time zone, so trips scheduled past midnight are included. Each processing step runs once per service day, looks back 3 days, and records its counts in `stop_arrival_runs` and `trip_outcome_runs`; rerunning a day replaces its rows.

## Report pages

- `/` — every route with tracked trips: trips tracked, share that did not run, share of stop arrivals on time, median delay.
- `/routes/{routeId}` — trips that did not run by day type (weekday, Saturday, Sunday) and time of day (scheduled start in agency local time), an arrival summary, and the 50 most recent trips that did not run with service date and trip ID.
- `/stops?q=` — stop and station search by name.
- `/stops/{stopId}` — arrival timeliness per route at a stop, including all platforms of a station.
- `/health` — 200 when the latest feed timestamp is at most 2 minutes old, 503 otherwise or when the database is unreachable.

Pages cover the last 7, 30, or 90 processed service days (`?days=`, default 30). Only `Observed` and `NotObserved` trips count as tracked. An arrival is on time from 1 minute early to 5 minutes late. Route and stop names come from the most recently imported schedule.

## Known limitations

- The first stop of each trip has no arrival; departures are not measured yet.
- Positions without a trip start date are not used.
- A terminal stop gets no arrival when the vehicle leaves the feed without reporting `STOPPED_AT`.
- Trips are matched by trip ID only; a trip reported under a different realtime trip ID looks missing.
- Cancellations announced in GTFS-Realtime TripUpdates are not read.
- Service days before `feed_polls` existed have no poll history, so their unobserved trips are recorded as `FeedGap`.
- Day types follow the day of the week; holidays are not distinguished.
- `vehicle_positions` is not partitioned and has no retention policy yet.

## Stack

- .NET 10 worker service (ingestion) and ASP.NET Core Razor Pages (web)
- PostgreSQL 17 with EF Core migrations; bulk paths use Npgsql directly
- Docker multi-target image and Docker Compose
- xUnit and Testcontainers
- GitHub Actions CI: build and all tests, plus a container job that builds the images, starts the stack, and requests `/health`, `/`, and the stop search page

## Repository layout

- `src/Transit.Gtfs` — GTFS-Realtime protobuf bindings (generated from `Protos/gtfs-realtime.proto`), vehicle position extraction, service-day time resolution, the static GTFS zip reader (`Static/`), stop arrival inference (`Arrivals/`), and feed coverage, outage, and trip outcome rules (`Reliability/`).
- `src/Transit.Data` — EF Core model and migrations, the vehicle position writer, static schedule importer, stop arrival, trip outcome, and feed outage recorders, and report queries (`Reports/`).
- `src/Transit.Ingestion` — worker that polls the realtime feed, refreshes the static schedule, processes completed service days, and monitors feed outages.
- `src/Transit.Web` — report pages and the `/health` endpoint.
- `tests/Transit.Gtfs.Tests` — time resolution, feed extraction, arrival inference, feed coverage, and outage detection.
- `tests/Transit.Ingestion.Tests` — feed client, service-day completion, and outage alerts.
- `tests/Transit.Data.Tests` — PostgreSQL integration tests for every writer and recorder (Docker required).
- `tests/Transit.Web.Tests` — page rendering against a seeded PostgreSQL container (Docker required).

## Getting started

Requires the .NET 10 SDK and Docker.

```
docker compose up -d
dotnet build
dotnet test
dotnet tool restore
dotnet ef database update --project src/Transit.Data --startup-project src/Transit.Web
dotnet run --project src/Transit.Ingestion
dotnet run --project src/Transit.Web
```

`docker-compose.yml` starts a local PostgreSQL that accepts connections from `127.0.0.1` without a password; use it for local development only. Without Docker, `Transit.Gtfs.Tests` and `Transit.Ingestion.Tests` still run; the Data and Web test projects need it.

Schema changes are made with `dotnet ef migrations add <Name> --project src/Transit.Data --startup-project src/Transit.Web`. Database identifiers use snake_case.

## Configuration

Settings for the worker are in `src/Transit.Ingestion/appsettings.json`:

- `GtfsRealtime` — vehicle positions URL and poll interval (MBTA, 20 seconds).
- `GtfsStatic` — schedule URL and refresh interval (MBTA, 6 hours).
- `ServiceDays` — processing interval (30 minutes), look-back (3 days), service day length (30 hours), maximum interpolation gap (3 minutes), and maximum feed gap for trip outcomes (2 minutes).
- `FeedHealth` — outage check interval (30 seconds), maximum feed gap (2 minutes), look-back (1 day), and optional `AlertWebhookUrl`. Alerts are posted as JSON with `text` and `content` fields, which Slack and Discord incoming webhooks accept.

The web app reads `FeedHealth:MaxFeedAge` (2 minutes) for `/health`. Both apps read the `Transit` connection string.

## Running the full stack in containers

The `Dockerfile` has three targets: `migrations` (an EF Core migration bundle), `ingestion`, and `web`. `compose.deploy.yml` runs PostgreSQL with a password, applies migrations, and then starts the worker and the web app:

```
cp .env.example .env   # set POSTGRES_PASSWORD, optionally ALERT_WEBHOOK_URL and WEB_PORT
docker compose -f compose.deploy.yml up -d --build
```

The web app listens on port 8080 over plain HTTP; put a TLS-terminating reverse proxy in front of it when exposing it publicly. Data persists in the `postgres-data` volume. The same compose file runs on any host with Docker, such as a small virtual server. Results only become meaningful after the worker has run continuously through complete service days.

## Milestones

- [x] Load static GTFS for one agency into PostgreSQL
- [x] Poll and persist vehicle positions with idempotent writes
- [x] Match positions to scheduled trips; compute per-stop arrival deltas
- [x] Detect trips that never ran for a completed service day
- [x] Containerize; add gap detection and alerts
- [ ] Deploy to a continuously running host with external uptime monitoring of `/health`
- [x] Public route and stop pages
- [ ] Compare results against published agency performance data (needs several weeks of collected data)
- [ ] Partition `vehicle_positions` by time and add a retention policy
- [ ] Replay tests from recorded MBTA feed snapshots
