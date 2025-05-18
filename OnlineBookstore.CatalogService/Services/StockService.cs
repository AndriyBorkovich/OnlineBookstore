using Microsoft.EntityFrameworkCore;
using OnlineBookstore.CatalogService.Data;
using OnlineBookstore.CatalogService.Models;

namespace OnlineBookstore.CatalogService.Services
{
    public class StockService
    {
        private readonly CatalogDbContext _dbContext;
        private readonly BookCacheService _cacheService;
        private readonly ILogger<StockService> _logger;
        
        // In-memory reservation tracking (could be replaced with Redis in production)
        private static readonly Dictionary<Guid, List<(Guid OrderId, int Quantity)>> _stockReservations = [];
        private static readonly SemaphoreSlim _semaphore = new(1, 1);

        public StockService(
            CatalogDbContext dbContext,
            BookCacheService cacheService,
            ILogger<StockService> logger)
        {
            _dbContext = dbContext;
            _cacheService = cacheService;
            _logger = logger;
        }

        public async Task<BookStockInfo?> GetBookStockInfoAsync(Guid bookId)
        {
            var book = await _dbContext.Books
                .Where(b => b.Id == bookId)
                .Select(b => new BookStockInfo(b.Id, b.Title, b.Stock))
                .FirstOrDefaultAsync();

            if (book == null)
            {
                _logger.LogWarning("Book with ID {BookId} not found when checking stock", bookId);
                return null;
            }

            return book;
        }

        public async Task<StockValidationResult> ValidateStockAsync(Guid bookId, int requestedQuantity)
        {
            try
            {
                var book = await _dbContext.Books.FindAsync(bookId);
                
                if (book == null)
                {
                    _logger.LogWarning("Book with ID {BookId} not found when validating stock", bookId);
                    return new StockValidationResult(false, 0);
                }

                // Get reserved quantity for this book
                var reservedQuantity = GetReservedQuantity(bookId);
                
                // Check if we have enough available stock
                var availableStock = book.Stock - reservedQuantity;
                var isAvailable = availableStock >= requestedQuantity;

                _logger.LogInformation(
                    "Stock validation for book {BookId}: Requested: {RequestedQuantity}, Total Stock: {TotalStock}, Reserved: {ReservedQuantity}, Available: {AvailableStock}, Result: {IsAvailable}",
                    bookId, requestedQuantity, book.Stock, reservedQuantity, availableStock, isAvailable);
                
                return new StockValidationResult(isAvailable, availableStock);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating stock for book {BookId}", bookId);
                throw;
            }
        }

        public async Task<StockReservationResult> ReserveStockAsync(Guid bookId, int quantity, Guid orderId)
        {
            try
            {
                // First validate if stock is available
                var validation = await ValidateStockAsync(bookId, quantity);
                if (!validation.IsAvailable)
                {
                    return new StockReservationResult(false, $"Insufficient stock. Available: {validation.AvailableStock}, Requested: {quantity}");
                }

                await _semaphore.WaitAsync();
                try
                {
                    // Add reservation
                    if (!_stockReservations.TryGetValue(bookId, out var reservations))
                    {
                        reservations = new List<(Guid OrderId, int Quantity)>();
                        _stockReservations[bookId] = reservations;
                    }

                    // Check if this order already has a reservation for this book
                    var existingReservation = reservations.FindIndex(r => r.OrderId == orderId);
                    if (existingReservation >= 0)
                    {
                        // Update existing reservation
                        reservations[existingReservation] = (orderId, quantity);
                    }
                    else
                    {
                        // Add new reservation
                        reservations.Add((orderId, quantity));
                    }
                    
                    _logger.LogInformation("Reserved {Quantity} units of book {BookId} for order {OrderId}", 
                        quantity, bookId, orderId);
                        
                    return new StockReservationResult(true, "Stock reserved successfully");
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reserving stock for book {BookId} for order {OrderId}", bookId, orderId);
                return new StockReservationResult(false, $"Error: {ex.Message}");
            }
        }

        public async Task<bool> CommitReservationAsync(Guid bookId, int quantity, Guid orderId)
        {
            try
            {
                await _semaphore.WaitAsync();
                try
                {
                    // Check if the reservation exists
                    if (!_stockReservations.TryGetValue(bookId, out var reservations) || 
                        !reservations.Any(r => r.OrderId == orderId))
                    {
                        _logger.LogWarning("No reservation found for book {BookId} for order {OrderId}", bookId, orderId);
                        return false;
                    }

                    // Remove reservation
                    reservations.RemoveAll(r => r.OrderId == orderId);
                    if (reservations.Count == 0)
                    {
                        _stockReservations.Remove(bookId);
                    }

                    // Update actual stock in database
                    var book = await _dbContext.Books.FindAsync(bookId);
                    if (book == null)
                    {
                        _logger.LogWarning("Book with ID {BookId} not found when committing reservation", bookId);
                        return false;
                    }

                    if (book.Stock < quantity)
                    {
                        _logger.LogError("Critical error: Book {BookId} has less stock than the reserved quantity", bookId);
                        return false;
                    }

                    book.Stock -= quantity;
                    await _dbContext.SaveChangesAsync();
                    
                    // Update cache
                    await _cacheService.RemoveBookFromCacheAsync(bookId);
                    
                    _logger.LogInformation("Committed reservation and reduced stock for book {BookId} by {Quantity} units", 
                        bookId, quantity);
                        
                    return true;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error committing reservation for book {BookId} for order {OrderId}", bookId, orderId);
                throw;
            }
        }

        public async Task<bool> CancelReservationAsync(Guid bookId, Guid orderId)
        {
            try
            {
                await _semaphore.WaitAsync();
                try
                {
                    // Check if the reservation exists
                    if (!_stockReservations.TryGetValue(bookId, out var reservations))
                    {
                        _logger.LogWarning("No reservations found for book {BookId} when cancelling for order {OrderId}", bookId, orderId);
                        return false;
                    }

                    var orderReservation = reservations.FirstOrDefault(r => r.OrderId == orderId);
                    if (orderReservation == default)
                    {
                        _logger.LogWarning("No reservation found for order {OrderId} for book {BookId}", orderId, bookId);
                        return false;
                    }

                    // Remove reservation
                    reservations.RemoveAll(r => r.OrderId == orderId);
                    if (reservations.Count == 0)
                    {
                        _stockReservations.Remove(bookId);
                    }
                    
                    _logger.LogInformation("Cancelled reservation for book {BookId} for order {OrderId}", bookId, orderId);
                    
                    return true;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling reservation for book {BookId} for order {OrderId}", bookId, orderId);
                throw;
            }
        }

        private int GetReservedQuantity(Guid bookId)
        {
            if (!_stockReservations.TryGetValue(bookId, out var reservations))
            {
                return 0;
            }

            return reservations.Sum(r => r.Quantity);
        }
    }

    // DTOs
    public record BookStockInfo(Guid Id, string Title, int Stock);
    
    public record StockValidationResult(bool IsAvailable, int AvailableStock);
    
    public record StockReservationResult(bool Success, string Message);
}