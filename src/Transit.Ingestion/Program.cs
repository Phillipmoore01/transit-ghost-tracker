using Transit.Data;
using Transit.Ingestion;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<GtfsRealtimeOptions>(builder.Configuration.GetSection(GtfsRealtimeOptions.SectionName));
builder.Services.AddHttpClient<GtfsRealtimeClient>();
builder.Services.AddTransitDatabase(builder.Configuration);
builder.Services.AddHostedService<VehiclePositionPoller>();

var host = builder.Build();
host.Run();
