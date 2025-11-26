using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using OnlineBookstore.CatalogService.Data;
using OnlineBookstore.CatalogService.Models;
using OnlineBookstore.CatalogService.Services;

namespace OnlineBookstore.UnitTests.CatalogService;

public class StockServiceTests : IDisposable
{
    private readonly CatalogDbContext _dbContext;
    private readonly Mock<BookCacheService> _cacheMock;
    private readonly Mock<ILogger<StockService>> _loggerMock;
    private readonly Mock<ILogger<BookCacheService>> _bookCacheLoggerMock;
    private readonly StockService _service;

    public StockServiceTests()
    {
        // Setup in-memory EF Core context
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dbContext = new CatalogDbContext(options);

        _bookCacheLoggerMock = new Mock<ILogger<BookCacheService>>();

        _cacheMock = new Mock<BookCacheService>(MockBehavior.Strict, null!, _bookCacheLoggerMock.Object);
        _loggerMock = new Mock<ILogger<StockService>>();

        _service = new StockService(_dbContext, _cacheMock.Object, _loggerMock.Object);
    }

    public void Dispose() => _dbContext.Dispose();

    [Fact]
    public async Task GetBookStockInfoAsync_ReturnsCorrectInfo_WhenBookExists()
    {
        // Arrange
        var book = new Book { Id = Guid.NewGuid(), Title = "Sample", Stock = 10 };
        _dbContext.Books.Add(book);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.GetBookStockInfoAsync(book.Id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(book.Id, result!.Id);
        Assert.Equal("Sample", result.Title);
        Assert.Equal(10, result.Stock);
    }

    [Fact]
    public async Task GetBookStockInfoAsync_ReturnsNull_WhenBookDoesNotExist()
    {
        // Act
        var result = await _service.GetBookStockInfoAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateStockAsync_ReturnsFalse_WhenBookDoesNotExist()
    {
        // Act
        var result = await _service.ValidateStockAsync(Guid.NewGuid(), 1);

        // Assert
        Assert.False(result.IsAvailable);
        Assert.Equal(0, result.AvailableStock);
    }

    [Fact]
    public async Task ValidateStockAsync_ReturnsTrue_WhenEnoughStockAndNoReservations()
    {
        // Arrange
        var book = new Book { Id = Guid.NewGuid(), Title = "Book", Stock = 5 };
        _dbContext.Books.Add(book);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.ValidateStockAsync(book.Id, 3);

        // Assert
        Assert.True(result.IsAvailable);
        Assert.Equal(5, result.AvailableStock);
    }

    [Fact]
    public async Task ValidateStockAsync_ReturnsFalse_WhenRequestedExceedsStock()
    {
        // Arrange
        var book = new Book { Id = Guid.NewGuid(), Title = "Book", Stock = 5 };
        _dbContext.Books.Add(book);
        await _dbContext.SaveChangesAsync();

        // Act
        var result = await _service.ValidateStockAsync(book.Id, 6);

        // Assert
        Assert.False(result.IsAvailable);
        Assert.Equal(5, result.AvailableStock);
    }

    [Fact]
    public async Task ReserveStockAsync_ReturnsSuccess_WhenSufficientStock()
    {
        // Arrange
        var book = new Book { Id = Guid.NewGuid(), Title = "Book", Stock = 5 };
        await _dbContext.Books.AddAsync(book);
        await _dbContext.SaveChangesAsync();
        var orderId = Guid.NewGuid();

        // Act
        var result = await _service.ReserveStockAsync(book.Id, 3, orderId);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ReserveStockAsync_ReturnsFailure_WhenInsufficientStock()
    {
        // Arrange
        var book = new Book { Id = Guid.NewGuid(), Title = "Book", Stock = 2 };
        await _dbContext.Books.AddAsync(book);
        await _dbContext.SaveChangesAsync();
        var orderId = Guid.NewGuid();

        // Act
        var result = await _service.ReserveStockAsync(book.Id, 5, orderId);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("Insufficient stock", result.Message);
    }

    [Fact]
    public async Task CommitReservationAsync_ReturnsTrueAndReducesStock_WhenReservationExists()
    {
        // Arrange
        var book = new Book { Id = Guid.NewGuid(), Title = "Book", Stock = 5 };
        await _dbContext.Books.AddAsync(book);
        await _dbContext.SaveChangesAsync();
        var orderId = Guid.NewGuid();

        // Reserve first
        var reserveResult = await _service.ReserveStockAsync(book.Id, 2, orderId);
        Assert.True(reserveResult.Success);

        // Act
        var commitResult = await _service.CommitReservationAsync(book.Id, 2, orderId);

        // Assert
        Assert.True(commitResult);

        var updated = await _dbContext.Books.FindAsync(book.Id);
        Assert.Equal(3, updated!.Stock);

        _cacheMock.Verify();
    }

    [Fact]
    public async Task CommitReservationAsync_ReturnsFalse_WhenNoReservation()
    {
        // Arrange
        var book = new Book { Id = Guid.NewGuid(), Title = "Book", Stock = 5 };
        await _dbContext.Books.AddAsync(book);
        await _dbContext.SaveChangesAsync();
        var orderId = Guid.NewGuid();

        // Act
        var commitResult = await _service.CommitReservationAsync(book.Id, 1, orderId);

        // Assert
        Assert.False(commitResult);
    }

    [Fact]
    public async Task CancelReservationAsync_ReturnsTrue_WhenReservationExists()
    {
        // Arrange
        var book = new Book { Id = Guid.NewGuid(), Title = "Book", Stock = 5 };
        await _dbContext.Books.AddAsync(book);
        await _dbContext.SaveChangesAsync();
        var orderId = Guid.NewGuid();

        // Reserve then cancel
        var reserveResult = await _service.ReserveStockAsync(book.Id, 1, orderId);
        Assert.True(reserveResult.Success);

        // Act
        var cancelResult = await _service.CancelReservationAsync(book.Id, orderId);

        // Assert
        Assert.True(cancelResult);

        // After cancel, should be able to reserve full stock again
        var validate = await _service.ValidateStockAsync(book.Id, 5);
        Assert.True(validate.IsAvailable);
    }

    [Fact]
    public async Task CancelReservationAsync_ReturnsFalse_WhenNoReservation()
    {
        // Arrange
        var bookId = Guid.NewGuid();

        // Act
        var cancelResult = await _service.CancelReservationAsync(bookId, Guid.NewGuid());

        // Assert
        Assert.False(cancelResult);
    }
}