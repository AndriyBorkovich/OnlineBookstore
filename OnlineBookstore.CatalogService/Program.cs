using Microsoft.EntityFrameworkCore;
using OnlineBookstore.CatalogService.Data;
using OnlineBookstore.CatalogService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Configure PostgreSQL with Aspire
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb", configureDbContextOptions: options =>
{
    options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
});

// Configure Redis caching with Aspire
builder.AddRedisDistributedCache("redis");
builder.Services.AddScoped<BookCacheService>();

// Configure Elasticsearch with Aspire
builder.AddElasticsearchClient("elasticsearch");
builder.Services.AddScoped<ElasticsearchService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    
    // Apply migrations in development
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
    dbContext.Database.Migrate();
    
    // Initialize Elasticsearch index
    var esService = scope.ServiceProvider.GetRequiredService<ElasticsearchService>();
    await esService.EnsureIndexCreatedAsync();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
