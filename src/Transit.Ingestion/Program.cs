using Transit.Data;
using Transit.Ingestion;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<GtfsRealtimeOptions>(builder.Configuration.GetSection(GtfsRealtimeOptions.SectionName));
builder.Services.Configure<GtfsStaticOptions>(builder.Configuration.GetSection(GtfsStaticOptions.SectionName));
builder.Services.Configure<ServiceDayOptions>(builder.Configuration.GetSection(ServiceDayOptions.SectionName));
builder.Services.Configure<FeedHealthOptions>(builder.Configuration.GetSection(FeedHealthOptions.SectionName));
builder.Services.AddHttpClient<GtfsRealtimeClient>();
builder.Services.AddHttpClient<GtfsScheduleClient>(http => http.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddHttpClient<OutageAlertSender>(http => http.Timeout = TimeSpan.FromSeconds(15));
builder.Services.AddTransitDatabase(builder.Configuration);
builder.Services.AddHostedService<VehiclePositionPoller>();
builder.Services.AddHostedService<StaticScheduleRefresher>();
builder.Services.AddHostedService<ServiceDayWorker>();
builder.Services.AddHostedService<FeedOutageMonitor>();

var host = builder.Build();
host.Run();
