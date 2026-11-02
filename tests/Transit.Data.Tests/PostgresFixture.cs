using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Transit.Data.Tests;

public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17").Build();

    public TransitDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<TransitDbContext>().UseNpgsql(container.GetConnectionString()).Options);

    public async Task InitializeAsync()
    {
        await container.StartAsync();

        await using var context = CreateContext();
        await context.Database.MigrateAsync();
    }

    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}
