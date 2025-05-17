using OnlineBookstore.ReviewService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddControllers();

// Configure MongoDB with Aspire
builder.AddMongoDBClient("mongodb");
builder.Services.AddSingleton<ReviewService.Services.ReviewService>();

// Configure Redis caching with Aspire
builder.AddRedisDistributedCache("redis");
builder.Services.AddSingleton<ReviewCacheService>();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
