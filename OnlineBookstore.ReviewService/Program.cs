using OnlineBookstore.ReviewService.Models;
using OnlineBookstore.ReviewService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

// Configure MongoDB with Aspire
builder.AddMongoDBClient("mongodb");
builder.Services.AddSingleton<ReviewService>();

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

// Define API groups
var reviewsApi = app.MapGroup("/api/reviews");

// Reviews endpoints
reviewsApi.MapGet("/", async (ReviewService reviewService, ReviewCacheService cacheService) => 
{
    // Try to get from cache first
    var recentReviews = await cacheService.GetRecentReviewsAsync();
    if (recentReviews != null)
    {
        return Results.Ok(recentReviews);
    }

    // Get from database if not in cache
    var reviews = await reviewService.GetReviewsAsync();
    
    // Cache the results
    await cacheService.CacheRecentReviewsAsync(reviews);

    return Results.Ok(reviews);
});

reviewsApi.MapGet("/{id}", async (string id, ReviewService reviewService) => 
{
    try
    {
        var review = await reviewService.GetReviewAsync(id);
        return review is null ? Results.NotFound() : Results.Ok(review);
    }
    catch (FormatException)
    {
        return Results.BadRequest("Invalid ID format");
    }
});

reviewsApi.MapGet("/book/{bookId:guid}", async (Guid bookId, ReviewService reviewService, ReviewCacheService cacheService) => 
{
    // Try to get from cache first
    var cachedReviews = await cacheService.GetBookReviewsAsync(bookId);
    if (cachedReviews != null)
    {
        return Results.Ok(cachedReviews);
    }

    // Get from database if not in cache
    var reviews = await reviewService.GetReviewsByBookIdAsync(bookId);
    
    // Cache the results
    await cacheService.CacheBookReviewsAsync(bookId, reviews);

    return Results.Ok(reviews);
});

reviewsApi.MapGet("/user/{userId:guid}", async (Guid userId, ReviewService reviewService) => 
{
    var reviews = await reviewService.GetReviewsByUserIdAsync(userId);
    return Results.Ok(reviews);
});

reviewsApi.MapGet("/book/{bookId:guid}/rating", async (Guid bookId, ReviewService reviewService, ReviewCacheService cacheService) => 
{
    // Try to get from cache first
    var cachedRating = await cacheService.GetBookRatingAsync(bookId);
    if (cachedRating.HasValue)
    {
        return Results.Ok(cachedRating.Value);
    }

    // Get from database if not in cache
    var rating = await reviewService.GetAverageRatingForBookAsync(bookId);
    
    // Cache the results
    await cacheService.CacheBookRatingAsync(bookId, rating);

    return Results.Ok(rating);
});

reviewsApi.MapPost("/", async (Review review, ReviewService reviewService, ReviewCacheService cacheService) =>
{
    await reviewService.CreateReviewAsync(review);
    
    // Invalidate cache for this book
    await cacheService.InvalidateBookCacheAsync(review.BookId);
    
    return Results.Created($"/api/reviews/{review.Id}", review);
});

reviewsApi.MapPut("/{id}", async (string id, Review review, ReviewService reviewService, ReviewCacheService cacheService) =>
{
    try
    {
        // Ensure ID matches
        if (id != review.Id.ToString())
        {
            return Results.BadRequest("ID mismatch");
        }
        
        await reviewService.UpdateReviewAsync(id, review);
        
        // Invalidate cache for this book
        await cacheService.InvalidateBookCacheAsync(review.BookId);
        
        return Results.NoContent();
    }
    catch (FormatException)
    {
        return Results.BadRequest("Invalid ID format");
    }
});

reviewsApi.MapDelete("/{id}", async (string id, ReviewService reviewService, ReviewCacheService cacheService) =>
{
    try
    {
        // Get the review first to know which book's cache to invalidate
        var review = await reviewService.GetReviewAsync(id);
        if (review is null)
        {
            return Results.NotFound();
        }
        
        await reviewService.DeleteReviewAsync(id);
        
        // Invalidate cache for this book
        await cacheService.InvalidateBookCacheAsync(review.BookId);
        
        return Results.NoContent();
    }
    catch (FormatException)
    {
        return Results.BadRequest("Invalid ID format");
    }
});

app.Run();
