using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;
using Transit.Gtfs.Reliability;

namespace Transit.Data;

public enum FeedOutageChange
{
    Started = 0,
    Ended = 1,
    FoundAfterItEnded = 2,
}

public sealed record FeedOutageEvent(FeedOutageChange Change, DateTimeOffset StartedAt, DateTimeOffset? EndedAt);

public class FeedOutageRecorder(TransitDbContext context, TimeProvider timeProvider)
{
    private const string FeedTimestampsSql = """
        SELECT feed_timestamp
        FROM feed_polls
        WHERE feed_timestamp >= coalesce(
            (SELECT max(feed_timestamp) FROM feed_polls WHERE feed_timestamp < @from),
            @from)
        """;

    private const string UpsertOutagesSql = """
        INSERT INTO feed_outages (started_at, ended_at, detected_at)
        SELECT started_at, ended_at, @detected_at
        FROM unnest(@started_ats, @ended_ats) AS o(started_at, ended_at)
        ON CONFLICT (started_at) DO UPDATE SET ended_at = excluded.ended_at
            WHERE feed_outages.ended_at IS NULL AND excluded.ended_at IS NOT NULL
        RETURNING (xmax = 0) AS inserted, started_at, ended_at
        """;

    public Task<DateTimeOffset?> GetLatestFeedTimestampAsync(CancellationToken cancellationToken) =>
        context.FeedPolls.MaxAsync(p => (DateTimeOffset?)p.FeedTimestamp, cancellationToken);

    public async Task<IReadOnlyList<FeedOutageEvent>> DetectAsync(
        TimeSpan lookback,
        TimeSpan maxGap,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        await context.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();

        var timestamps = new List<DateTimeOffset>();
        await using (var command = new NpgsqlCommand(FeedTimestampsSql, connection))
        {
            command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = now - lookback });

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                timestamps.Add(reader.GetFieldValue<DateTimeOffset>(0));
            }
        }

        var outages = FeedOutageDetector.Find(timestamps, now, maxGap);
        if (outages.Count == 0)
        {
            return [];
        }

        await using var upsert = new NpgsqlCommand(UpsertOutagesSql, connection);
        upsert.Parameters.Add(new NpgsqlParameter("detected_at", NpgsqlDbType.TimestampTz) { Value = now });
        upsert.Parameters.Add(new NpgsqlParameter("started_ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
        {
            Value = outages.Select(o => o.LastFeedBefore.ToUniversalTime()).ToArray(),
        });
        upsert.Parameters.Add(new NpgsqlParameter("ended_ats", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz)
        {
            Value = outages.Select(o => o.FirstFeedAfter?.ToUniversalTime()).ToArray(),
        });

        var events = new List<FeedOutageEvent>();
        await using var changed = await upsert.ExecuteReaderAsync(cancellationToken);
        while (await changed.ReadAsync(cancellationToken))
        {
            var inserted = changed.GetBoolean(0);
            var endedAt = changed.IsDBNull(2) ? (DateTimeOffset?)null : changed.GetFieldValue<DateTimeOffset>(2);
            var change = (inserted, endedAt) switch
            {
                (true, null) => FeedOutageChange.Started,
                (true, _) => FeedOutageChange.FoundAfterItEnded,
                _ => FeedOutageChange.Ended,
            };

            events.Add(new FeedOutageEvent(change, changed.GetFieldValue<DateTimeOffset>(1), endedAt));
        }

        return events.OrderBy(e => e.StartedAt).ToArray();
    }
}
