using Transit.Data;
using Transit.Ingestion;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<GtfsRealtimeOptions>(builder.Configuration.GetSection(GtfsRealtimeOptions.SectionName));
builder.Services.Configure<GtfsStaticOptions>(builder.Configuration.GetSection(GtfsStaticOptions.SectionName));
builder.Services.Configure<StopArrivalOptions>(builder.Configuration.GetSection(StopArrivalOptions.SectionName));
builder.Services.AddHttpClient<GtfsRealtimeClient>();
builder.Services.AddHttpClient<GtfsScheduleClient>(http => http.Timeout = TimeSpan.FromMinutes(5));
builder.Services.AddTransitDatabase(builder.Configuration);
builder.Services.AddHostedService<VehiclePositionPoller>();
builder.Services.AddHostedService<StaticScheduleRefresher>();
builder.Services.AddHostedService<StopArrivalWorker>();

var host = builder.Build();
host.Run();
