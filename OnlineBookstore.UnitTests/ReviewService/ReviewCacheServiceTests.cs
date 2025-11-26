using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Moq;
using OnlineBookstore.ReviewService.Models;
using OnlineBookstore.ReviewService.Services;
using System.Text;
using System.Text.Json;

namespace OnlineBookstore.UnitTests.ReviewService;

public sealed class ReviewCacheServiceTests
{
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<ReviewCacheService>> _loggerMock;
    private readonly ReviewCacheService _service;

    public ReviewCacheServiceTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<ReviewCacheService>>();
        _service = new ReviewCacheService(_cacheMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CacheRecentReviewsAsync_CallsSetAsync()
    {
        // Arrange
        var reviews = new List<Review>
        {
            new() { Id = ObjectId.GenerateNewId(), BookId = Guid.NewGuid(), UserId = Guid.NewGuid(), Rating = 5, Comment = "Great!", CreatedAt = DateTime.UtcNow }
        };

        // Act
        await _service.CacheRecentReviewsAsync(reviews);

        // Assert
        _cacheMock.Verify(c => c.SetAsync(
                "recent_reviews",
                It.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("\"Comment\":\"Great!\"")),
                It.Is<DistributedCacheEntryOptions>(opt =>
                    opt.AbsoluteExpirationRelativeToNow.HasValue &&
                    opt.AbsoluteExpirationRelativeToNow.Value.TotalMinutes == 15),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetRecentReviewsAsync_ReturnsNull_WhenCacheEmpty()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync("recent_reviews", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null);

        // Act
        var result = await _service.GetRecentReviewsAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecentReviewsAsync_ReturnsList_WhenDataExists()
    {
        // Arrange
        var reviews = new List<Review>
        {
            new() { Id = ObjectId.GenerateNewId(), BookId = Guid.NewGuid(), UserId = Guid.NewGuid(), Rating = 4, Comment = "Nice", CreatedAt = DateTime.UtcNow }
        };
        var json = JsonSerializer.Serialize(reviews);
        var bytes = Encoding.UTF8.GetBytes(json);

        _cacheMock
            .Setup(c => c.GetAsync("recent_reviews", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await _service.GetRecentReviewsAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal("Nice", result[0].Comment);
    }

    [Fact]
    public async Task CacheBookReviewsAsync_CallsSetAsync()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var reviews = new List<Review>
        {
            new Review { Id = ObjectId.GenerateNewId(), BookId = bookId, UserId = Guid.NewGuid(), Rating = 3, Comment = "Ok", CreatedAt = DateTime.UtcNow }
        };

        // Act
        await _service.CacheBookReviewsAsync(bookId, reviews);

        // Assert
        var expectedKey = $"book:{bookId}:reviews";
        _cacheMock.Verify(c => c.SetAsync(
                expectedKey,
                It.Is<byte[]>(b => Encoding.UTF8.GetString(b).Contains("\"Ok\"")),
                It.Is<DistributedCacheEntryOptions>(opt =>
                    opt.AbsoluteExpirationRelativeToNow.HasValue &&
                    opt.AbsoluteExpirationRelativeToNow.Value.TotalMinutes == 15),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBookReviewsAsync_ReturnsNull_WhenCacheEmpty()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        _cacheMock
            .Setup(c => c.GetAsync($"book:{bookId}:reviews", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null);

        // Act
        var result = await _service.GetBookReviewsAsync(bookId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBookReviewsAsync_ReturnsList_WhenDataExists()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var reviews = new List<Review>
        {
            new() { Id = ObjectId.GenerateNewId(), BookId = bookId, UserId = Guid.NewGuid(), Rating = 2, Comment = "Bad", CreatedAt = DateTime.UtcNow }
        };
        var json = JsonSerializer.Serialize(reviews);
        var bytes = Encoding.UTF8.GetBytes(json);

        _cacheMock
            .Setup(c => c.GetAsync($"book:{bookId}:reviews", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await _service.GetBookReviewsAsync(bookId);

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(2, result[0].Rating);
    }

    [Fact]
    public async Task CacheBookRatingAsync_CallsSetAsync()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var rating = 4.5;

        // Act
        await _service.CacheBookRatingAsync(bookId, rating);

        // Assert
        _cacheMock.Verify(c => c.SetAsync(
                $"book:{bookId}:rating",
                It.Is<byte[]>(b => Encoding.UTF8.GetString(b) == rating.ToString()),
                It.Is<DistributedCacheEntryOptions>(opt =>
                    opt.AbsoluteExpirationRelativeToNow.HasValue &&
                    opt.AbsoluteExpirationRelativeToNow.Value.TotalMinutes == 15),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBookRatingAsync_ReturnsNull_WhenCacheEmpty()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        _cacheMock
            .Setup(c => c.GetAsync($"book:{bookId}:rating", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null);

        // Act
        var result = await _service.GetBookRatingAsync(bookId);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBookRatingAsync_ReturnsValue_WhenDataExists()
    {
        // Arrange
        var bookId = Guid.NewGuid();
        var rating = 3.7;
        var bytes = Encoding.UTF8.GetBytes(rating.ToString());

        _cacheMock
            .Setup(c => c.GetAsync($"book:{bookId}:rating", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await _service.GetBookRatingAsync(bookId);

        // Assert
        Assert.Equal(rating, result);
    }

    [Fact]
    public async Task InvalidateBookCacheAsync_CallsRemoveAsync_ForAllKeys()
    {
        // Arrange
        var bookId = Guid.NewGuid();

        // Act
        await _service.InvalidateBookCacheAsync(bookId);

        // Assert
        _cacheMock.Verify(c => c.RemoveAsync($"book:{bookId}:reviews", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync($"book:{bookId}:rating", It.IsAny<CancellationToken>()), Times.Once);
        _cacheMock.Verify(c => c.RemoveAsync("recent_reviews", It.IsAny<CancellationToken>()), Times.Once);
    }
}
