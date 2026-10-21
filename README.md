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

## Milestones

- [ ] Load static GTFS for one agency into PostgreSQL
- [ ] Poll and persist vehicle positions with idempotent writes
- [ ] Match positions to scheduled trips; compute per-stop arrival deltas
- [ ] Detect trips that never ran for a completed service day
- [ ] Containerize and deploy; add gap detection and alerts
- [ ] Public route and stop pages
- [ ] Compare results against published agency performance data
