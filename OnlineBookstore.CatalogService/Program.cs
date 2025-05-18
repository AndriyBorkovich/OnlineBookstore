using Microsoft.EntityFrameworkCore;
using OnlineBookstore.CatalogService.Data;
using OnlineBookstore.CatalogService.Models;
using OnlineBookstore.CatalogService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Configure PostgreSQL with Aspire
builder.AddNpgsqlDbContext<CatalogDbContext>("catalogdb");
// Configure Redis caching with Aspire
builder.AddRedisDistributedCache("redis");
builder.Services.AddScoped<BookCacheService>();

// Configure Elasticsearch with Aspire
builder.AddElasticsearchClient("elasticsearch");
builder.Services.AddScoped<ElasticsearchService>();

// Add stock service
builder.Services.AddScoped<StockService>();

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

// Define API groups
var booksApi = app.MapGroup("/api/books");
var categoriesApi = app.MapGroup("/api/categories");

// Books endpoints
booksApi.MapGet("/", async (CatalogDbContext db, BookCacheService cacheService) => 
{
    // Try to get from cache first
    var popularBooks = await cacheService.GetPopularBooksAsync();
    if (popularBooks != null)
    {
        return Results.Ok(popularBooks);
    }

    // Get from database if not in cache
    var books = await db.Books
        .Include(b => b.Category)
        .Take(20)
        .ToListAsync();

    // Cache the results
    await cacheService.CachePopularBooksAsync(books);

    return Results.Ok(books);
});

booksApi.MapGet("/{id:guid}", async (Guid id, CatalogDbContext db, BookCacheService cacheService) =>
{
    // Try to get from cache first
    var book = await cacheService.GetBookAsync(id);
    if (book != null)
    {
        return Results.Ok(book);
    }

    // Get from database if not in cache
    book = await db.Books
        .Include(b => b.Category)
        .FirstOrDefaultAsync(b => b.Id == id);

    if (book == null)
    {
        return Results.NotFound();
    }

    // Cache the book
    await cacheService.CacheBookAsync(book);

    return Results.Ok(book);
});

booksApi.MapGet("/{id:guid}/stock", async (Guid id, StockService stockService) =>
{
    var stockInfo = await stockService.GetBookStockInfoAsync(id);
    if (stockInfo == null)
    {
        return Results.NotFound();
    }

    return Results.Ok(stockInfo);
});

// Stock validation and reservation endpoints
booksApi.MapPost("/validate-stock", async (StockValidationRequest request, StockService stockService) =>
{
    var result = await stockService.ValidateStockAsync(request.BookId, request.Quantity);
    return Results.Ok(new StockValidationResponse(result.IsAvailable, result.AvailableStock));
});

booksApi.MapPost("/reserve-stock", async (StockReservationRequest request, StockService stockService) =>
{
    var result = await stockService.ReserveStockAsync(request.BookId, request.Quantity, request.OrderId);
    return Results.Ok(new StockReservationResponse(result.Success, result.Message));
});

booksApi.MapPost("/commit-stock", async (StockCommitRequest request, StockService stockService) =>
{
    var success = await stockService.CommitReservationAsync(request.BookId, request.Quantity, request.OrderId);
    return success ? Results.Ok() : Results.BadRequest("Failed to commit stock reservation");
});

booksApi.MapPost("/cancel-stock", async (StockCancelRequest request, StockService stockService) =>
{
    var success = await stockService.CancelReservationAsync(request.BookId, request.OrderId);
    return success ? Results.Ok() : Results.BadRequest("Failed to cancel stock reservation");
});

booksApi.MapGet("/search", async (string query, CatalogDbContext db, ElasticsearchService elasticsearchService) =>
{
    if (string.IsNullOrWhiteSpace(query))
    {
        var defaultBooks = await db.Books
            .AsNoTracking()
            .Include(b => b.Category)
            .Take(20)
            .ToListAsync();
        return Results.Ok(defaultBooks);
    }

    // Search in Elasticsearch
    var searchResults = await elasticsearchService.SearchBooksAsync(query);
    
    // If results from Elasticsearch, return them
    if (searchResults.Any())
    {
        return Results.Ok(searchResults);
    }
    
    // Fallback to database search if Elasticsearch has no results
    var books = await db.Books
        .Include(b => b.Category)
        .Where(b => b.Title.Contains(query) || b.Author.Contains(query) || b.Description.Contains(query))
        .Take(20)
        .ToListAsync();
        
    return Results.Ok(books);
});

booksApi.MapPost("/", async (Book book, CatalogDbContext db, ElasticsearchService elasticsearchService) =>
{
    db.Books.Add(book);
    await db.SaveChangesAsync();

    // Index in Elasticsearch
    await elasticsearchService.IndexBookAsync(book);

    return Results.Created($"/api/books/{book.Id}", book);
});

booksApi.MapPut("/{id:guid}", async (Guid id, Book book, CatalogDbContext db, BookCacheService cacheService, ElasticsearchService elasticsearchService) =>
{
    if (id != book.Id)
    {
        return Results.BadRequest();
    }

    db.Entry(book).State = EntityState.Modified;

    try
    {
        await db.SaveChangesAsync();
        
        // Update in Elasticsearch
        await elasticsearchService.IndexBookAsync(book);
        
        // Update in cache
        await cacheService.CacheBookAsync(book);
    }
    catch (DbUpdateConcurrencyException)
    {
        if (!await db.Books.AnyAsync(b => b.Id == id))
        {
            return Results.NotFound();
        }
        throw;
    }

    return Results.NoContent();
});

booksApi.MapDelete("/{id:guid}", async (Guid id, CatalogDbContext db, BookCacheService cacheService) =>
{
    var book = await db.Books.FindAsync(id);
    if (book == null)
    {
        return Results.NotFound();
    }

    db.Books.Remove(book);
    await db.SaveChangesAsync();
    
    // Remove from cache
    await cacheService.RemoveBookFromCacheAsync(id);

    return Results.NoContent();
});

// Categories endpoints
categoriesApi.MapGet("/", async (CatalogDbContext db) => 
    await db.Categories.ToListAsync());

categoriesApi.MapGet("/{id:guid}", async (Guid id, CatalogDbContext db) =>
{
    var category = await db.Categories.FindAsync(id);
    return category is null ? Results.NotFound() : Results.Ok(category);
});

categoriesApi.MapGet("/{id:guid}/books", async (Guid id, CatalogDbContext db) =>
{
    var category = await db.Categories
        .Include(c => c.Books)
        .FirstOrDefaultAsync(c => c.Id == id);

    if (category is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(category.Books.ToList());
});

categoriesApi.MapPost("/", async (Category category, CatalogDbContext db) =>
{
    db.Categories.Add(category);
    await db.SaveChangesAsync();
    return Results.Created($"/api/categories/{category.Id}", category);
});

categoriesApi.MapPut("/{id:guid}", async (Guid id, Category category, CatalogDbContext db) =>
{
    if (id != category.Id)
    {
        return Results.BadRequest();
    }

    db.Entry(category).State = EntityState.Modified;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == id))
        {
            return Results.NotFound();
        }
        throw;
    }

    return Results.NoContent();
});

categoriesApi.MapDelete("/{id:guid}", async (Guid id, CatalogDbContext db) =>
{
    var category = await db.Categories.FindAsync(id);
    if (category is null)
    {
        return Results.NotFound();
    }

    db.Categories.Remove(category);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

// Stock validation request/response types
public record StockValidationRequest(Guid BookId, int Quantity);
public record StockValidationResponse(bool IsAvailable, int AvailableStock);
public record StockReservationRequest(Guid BookId, int Quantity, Guid OrderId);
public record StockReservationResponse(bool Success, string Message);
public record StockCommitRequest(Guid BookId, int Quantity, Guid OrderId);
public record StockCancelRequest(Guid BookId, Guid OrderId);
