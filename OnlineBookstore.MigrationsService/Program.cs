using OnlineBookstore.CatalogService.Data;
using OnlineBookstore.MigrationsService;
using OnlineBookstore.OrderService.Data;

var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();

builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");
builder.AddNpgsqlDbContext<OrderDbContext>("orderdb");

builder.Services.AddHostedService<Worker>();
builder.Services.AddOpenTelemetry()
           .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

var host = builder.Build();
host.Run();

