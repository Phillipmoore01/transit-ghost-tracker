using Microsoft.EntityFrameworkCore;

namespace Transit.Data;

public class TransitDbContext(DbContextOptions<TransitDbContext> options) : DbContext(options)
{
    public DbSet<VehiclePosition> VehiclePositions => Set<VehiclePosition>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VehiclePosition>(position =>
        {
            position.ToTable("vehicle_positions");

            position.Property(p => p.VehicleId).HasMaxLength(64);
            position.Property(p => p.TripId).HasMaxLength(128);
            position.Property(p => p.RouteId).HasMaxLength(64);
            position.Property(p => p.StopId).HasMaxLength(64);

            position.HasIndex(p => new { p.VehicleId, p.ObservedAt }).IsUnique();
            position.HasIndex(p => new { p.TripId, p.StartDate });
            position.HasIndex(p => p.ObservedAt);
        });
    }
}
