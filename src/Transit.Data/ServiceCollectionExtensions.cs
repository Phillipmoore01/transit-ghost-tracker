using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Transit.Data;

public static class ServiceCollectionExtensions
{
    public const string ConnectionStringName = "Transit";

    public static IServiceCollection AddTransitDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException($"Connection string '{ConnectionStringName}' is not configured.");

        return services.AddDbContext<TransitDbContext>(options => options.UseNpgsql(connectionString));
    }
}
