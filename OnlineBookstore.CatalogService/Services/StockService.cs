using Microsoft.EntityFrameworkCore;
using OnlineBookstore.CatalogService.Data;

namespace OnlineBookstore.CatalogService.Services
{
    public sealed class StockService(
        CatalogDbContext dbContext,
        BookCacheService cacheService,
        ILogger<StockService> logger)
    {

        // In-memory reservation tracking (could be replaced with Redis in production)
        private static readonly Dictionary<Guid, List<(Guid OrderId, int Quantity)>> _stockReservations = [];
        private static readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<BookStockInfo?> GetBookStockInfoAsync(Guid bookId)
        {
            var book = await dbContext.Books
                .Where(b => b.Id == bookId)
                .Select(b => new BookStockInfo(b.Id, b.Title, b.Stock))
                .FirstOrDefaultAsync();

            if (book == null)
            {
                logger.LogWarning("Book with ID {BookId} not found when checking stock", bookId);
                return null;
            }

            return book;
        }

        public async Task<StockValidationResult> ValidateStockAsync(Guid bookId, int requestedQuantity)
        {
            try
            {
                var book = await dbContext.Books.FindAsync(bookId);
                
                if (book == null)
                {
                    logger.LogWarning("Book with ID {BookId} not found when validating stock", bookId);
                    return new StockValidationResult(false, 0);
                }

                var reservedQuantity = GetReservedQuantity(bookId);
                
                var availableStock = book.Stock - reservedQuantity;
                var isAvailable = availableStock >= requestedQuantity;

                logger.LogInformation(
                    "Stock validation for book {BookId}: Requested: {RequestedQuantity}, Total Stock: {TotalStock}, Reserved: {ReservedQuantity}, Available: {AvailableStock}, Result: {IsAvailable}",
                    bookId, requestedQuantity, book.Stock, reservedQuantity, availableStock, isAvailable);
                
                return new StockValidationResult(isAvailable, availableStock);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error validating stock for book {BookId}", bookId);
                throw;
            }
        }

        public async Task<StockReservationResult> ReserveStockAsync(Guid bookId, int quantity, Guid orderId)
        {
            await _semaphore.WaitAsync();

            try
            {
                var validation = await ValidateStockAsync(bookId, quantity);
                if (!validation.IsAvailable)
                {
                    return new StockReservationResult(false, $"Insufficient stock. Available: {validation.AvailableStock}, Requested: {quantity}");
                }

                if (!_stockReservations.TryGetValue(bookId, out var reservations))
                {
                    reservations = [];
                    _stockReservations[bookId] = reservations;
                }

                var existingReservation = reservations.FindIndex(r => r.OrderId == orderId);
                if (existingReservation >= 0)
                {
                    reservations[existingReservation] = (orderId, quantity);
                }
                else
                {
                    reservations.Add((orderId, quantity));
                }

                logger.LogInformation("Reserved {Quantity} units of book {BookId} for order {OrderId}",
                    quantity, bookId, orderId);

                return new StockReservationResult(true, "Stock reserved successfully");
               
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reserving stock for book {BookId} for order {OrderId}", bookId, orderId);
                return new StockReservationResult(false, $"Error: {ex.Message}");
            }
            finally
            {
                _semaphore.Release();
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
                        logger.LogWarning("No reservation found for book {BookId} for order {OrderId}", bookId, orderId);
                        return false;
                    }

                    // Remove reservation
                    reservations.RemoveAll(r => r.OrderId == orderId);
                    if (reservations.Count == 0)
                    {
                        _stockReservations.Remove(bookId);
                    }

                    // Update actual stock in database
                    var book = await dbContext.Books.FindAsync(bookId);
                    if (book == null)
                    {
                        logger.LogWarning("Book with ID {BookId} not found when committing reservation", bookId);
                        return false;
                    }

                    if (book.Stock < quantity)
                    {
                        logger.LogError("Book {BookId} has less stock than the reserved quantity", bookId);
                        return false;
                    }

                    book.Stock -= quantity;
                    await dbContext.SaveChangesAsync();
                    
                    // Update cache
                    await cacheService.RemoveBookFromCacheAsync(bookId);
                    
                    logger.LogInformation("Committed reservation and reduced stock for book {BookId} by {Quantity} units", 
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
                logger.LogError(ex, "Error committing reservation for book {BookId} for order {OrderId}", bookId, orderId);
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
                        logger.LogWarning("No reservations found for book {BookId} when cancelling for order {OrderId}", bookId, orderId);
                        return false;
                    }

                    var orderReservation = reservations.FirstOrDefault(r => r.OrderId == orderId);
                    if (orderReservation == default)
                    {
                        logger.LogWarning("No reservation found for order {OrderId} for book {BookId}", orderId, bookId);
                        return false;
                    }

                    // Remove reservation
                    reservations.RemoveAll(r => r.OrderId == orderId);
                    if (reservations.Count == 0)
                    {
                        _stockReservations.Remove(bookId);
                    }
                    
                    logger.LogInformation("Cancelled reservation for book {BookId} for order {OrderId}", bookId, orderId);
                    
                    return true;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error cancelling reservation for book {BookId} for order {OrderId}", bookId, orderId);
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