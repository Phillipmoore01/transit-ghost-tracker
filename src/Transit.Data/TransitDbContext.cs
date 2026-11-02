using Microsoft.EntityFrameworkCore;

namespace Transit.Data;

public class TransitDbContext(DbContextOptions<TransitDbContext> options) : DbContext(options)
{
    public DbSet<VehiclePosition> VehiclePositions => Set<VehiclePosition>();

    public DbSet<GtfsFeed> GtfsFeeds => Set<GtfsFeed>();

    public DbSet<Agency> Agencies => Set<Agency>();

    public DbSet<Route> Routes => Set<Route>();

    public DbSet<Stop> Stops => Set<Stop>();

    public DbSet<Trip> Trips => Set<Trip>();

    public DbSet<StopTime> StopTimes => Set<StopTime>();

    public DbSet<ServiceCalendar> ServiceCalendars => Set<ServiceCalendar>();

    public DbSet<CalendarDate> CalendarDates => Set<CalendarDate>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VehiclePosition>(position =>
        {
            position.Property(p => p.VehicleId).HasMaxLength(64);
            position.Property(p => p.TripId).HasMaxLength(128);
            position.Property(p => p.RouteId).HasMaxLength(64);
            position.Property(p => p.StopId).HasMaxLength(64);

            position.HasIndex(p => new { p.VehicleId, p.ObservedAt }).IsUnique();
            position.HasIndex(p => new { p.TripId, p.StartDate });
            position.HasIndex(p => p.ObservedAt);
        });

        modelBuilder.Entity<GtfsFeed>(feed =>
        {
            feed.Property(f => f.Sha256).HasMaxLength(64);
            feed.HasIndex(f => f.Sha256).IsUnique();
        });

        modelBuilder.Entity<Agency>(agency =>
        {
            agency.HasIndex(a => new { a.FeedId, a.AgencyId }).IsUnique();
            agency.HasOne<GtfsFeed>().WithMany().HasForeignKey(a => a.FeedId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Route>(route =>
        {
            route.HasKey(r => new { r.FeedId, r.RouteId });
            route.HasOne<GtfsFeed>().WithMany().HasForeignKey(r => r.FeedId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Stop>(stop =>
        {
            stop.HasKey(s => new { s.FeedId, s.StopId });
            stop.HasOne<GtfsFeed>().WithMany().HasForeignKey(s => s.FeedId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Trip>(trip =>
        {
            trip.HasKey(t => new { t.FeedId, t.TripId });
            trip.HasIndex(t => new { t.FeedId, t.RouteId });
            trip.HasIndex(t => new { t.FeedId, t.ServiceId });
            trip.HasOne<GtfsFeed>().WithMany().HasForeignKey(t => t.FeedId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StopTime>(stopTime =>
        {
            stopTime.HasKey(s => new { s.FeedId, s.TripId, s.StopSequence });
            stopTime.HasIndex(s => new { s.FeedId, s.StopId });
            stopTime.HasOne<GtfsFeed>().WithMany().HasForeignKey(s => s.FeedId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ServiceCalendar>(calendar =>
        {
            calendar.HasKey(c => new { c.FeedId, c.ServiceId });
            calendar.HasOne<GtfsFeed>().WithMany().HasForeignKey(c => c.FeedId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CalendarDate>(calendarDate =>
        {
            calendarDate.HasKey(c => new { c.FeedId, c.ServiceId, c.Date });
            calendarDate.HasOne<GtfsFeed>().WithMany().HasForeignKey(c => c.FeedId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
