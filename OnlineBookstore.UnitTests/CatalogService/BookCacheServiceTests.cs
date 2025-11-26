using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using OnlineBookstore.CatalogService.Models;
using OnlineBookstore.CatalogService.Services;
using System.Text;
using System.Text.Json;

namespace OnlineBookstore.UnitTests.CatalogService;

public sealed class BookCacheServiceTests
{
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<BookCacheService>> _loggerMock;
    private readonly BookCacheService _service;

    public BookCacheServiceTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<BookCacheService>>();
        _service = new BookCacheService(_cacheMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task CacheBookAsync_CallsSetAsync()
    {
        // Arrange
        var book = new Book
        {
            Id = Guid.NewGuid(),
            Title = "Test Title",
            Author = "Author",
            Description = "Desc",
            CategoryId = Guid.NewGuid(),
            Price = 9.99m,
            Stock = 5,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Act
        await _service.CacheBookAsync(book);

        // Assert
        _cacheMock.Verify(c => c.SetAsync(
                $"book:{book.Id}",
                It.Is<byte[]>(bytes =>
                    Encoding.UTF8.GetString(bytes).Contains("Test Title")),
                It.Is<DistributedCacheEntryOptions>(opt =>
                    opt.AbsoluteExpirationRelativeToNow.HasValue &&
                    opt.AbsoluteExpirationRelativeToNow.Value.TotalMinutes == 15),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetBookAsync_ReturnsNull_WhenCacheIsEmpty()
    {
        // Arrange
        var id = Guid.NewGuid();
        _cacheMock
            .Setup(c => c.GetAsync($"book:{id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null);

        // Act
        var result = await _service.GetBookAsync(id);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetBookAsync_ReturnsBook_WhenDataExists()
    {
        // Arrange
        var book = new Book
        {
            Id = Guid.NewGuid(),
            Title = "Sample",
            Author = "Author",
            Description = "Desc",
            CategoryId = Guid.NewGuid(),
            Price = 5.50m,
            Stock = 2,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(book);
        var bytes = Encoding.UTF8.GetBytes(json);
        _cacheMock
            .Setup(c => c.GetAsync($"book:{book.Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await _service.GetBookAsync(book.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(book.Id, result.Id);
        Assert.Equal(book.Title, result.Title);
        Assert.Equal(book.Author, result.Author);
    }

    [Fact]
    public async Task CachePopularBooksAsync_CallsSetAsync()
    {
        // Arrange
        var books = new List<Book>
            {
                new Book
                {
                    Id = Guid.NewGuid(),
                    Title = "Popular",
                    Author = "Author",
                    Description = "Desc",
                    CategoryId = Guid.NewGuid(),
                    Price = 7.77m,
                    Stock = 3,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            };

        // Act
        await _service.CachePopularBooksAsync(books);

        // Assert
        _cacheMock.Verify(c => c.SetAsync(
                "popular_books",
                It.Is<byte[]>(bytes =>
                    Encoding.UTF8.GetString(bytes).Contains("Popular")),
                It.Is<DistributedCacheEntryOptions>(opt =>
                    opt.AbsoluteExpirationRelativeToNow.HasValue &&
                    opt.AbsoluteExpirationRelativeToNow.Value.TotalMinutes == 15),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPopularBooksAsync_ReturnsNull_WhenCacheIsEmpty()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync("popular_books", It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[])null);

        // Act
        var result = await _service.GetPopularBooksAsync();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPopularBooksAsync_ReturnsList_WhenDataExists()
    {
        // Arrange
        var books = new List<Book>
            {
                new Book
                {
                    Id = Guid.NewGuid(),
                    Title = "Top",
                    Author = "Author",
                    Description = "Desc",
                    CategoryId = Guid.NewGuid(),
                    Price = 3.33m,
                    Stock = 1,
                    CreatedAt = DateTimeOffset.UtcNow
                }
            };
        var json = JsonSerializer.Serialize(books);
        var bytes = Encoding.UTF8.GetBytes(json);
        _cacheMock
            .Setup(c => c.GetAsync("popular_books", It.IsAny<CancellationToken>()))
            .ReturnsAsync(bytes);

        // Act
        var result = await _service.GetPopularBooksAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(books[0].Id, result[0].Id);
    }

    [Fact]
    public async Task RemoveBookFromCacheAsync_CallsRemoveAsync()
    {
        // Arrange
        var id = Guid.NewGuid();

        // Act
        await _service.RemoveBookFromCacheAsync(id);

        // Assert
        _cacheMock.Verify(c => c.RemoveAsync(
                $"book:{id}",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
