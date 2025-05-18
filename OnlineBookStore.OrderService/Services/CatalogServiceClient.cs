using System.Net;
using OnlineBookstore.OrderService.Models;

namespace OnlineBookstore.OrderService.Services
{
    public class CatalogServiceClient(
        HttpClient httpClient,
        ILogger<CatalogServiceClient> logger)
    {
        public record BookStockInfo(Guid Id, string Title, int Stock);
        
        public record StockValidationRequest(Guid BookId, int Quantity);
        
        public record StockValidationResponse(bool IsAvailable, int CurrentStock);
        
        public record StockReservationRequest(Guid BookId, int Quantity, Guid OrderId);
        
        public record StockReservationResponse(bool Success, string Message);

        public async Task<BookStockInfo?> GetBookStockInfoAsync(Guid bookId, CancellationToken cancellationToken = default)
        {
            try
            {
                return await httpClient.GetFromJsonAsync<BookStockInfo>($"/api/books/{bookId}/stock", cancellationToken);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogWarning("Book with ID {BookId} not found", bookId);
                return null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting book stock info for {BookId}", bookId);
                throw;
            }
        }

        public async Task<StockValidationResponse> ValidateStockAsync(StockValidationRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await httpClient.PostAsJsonAsync("/api/books/validate-stock", request, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<StockValidationResponse>(cancellationToken: cancellationToken) 
                           ?? new StockValidationResponse(false, 0);
                }
                
                logger.LogWarning("Stock validation failed with status code {StatusCode}", response.StatusCode);
                return new StockValidationResponse(false, 0);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error validating stock for book {BookId}", request.BookId);
                throw;
            }
        }

        public async Task<StockReservationResponse> ReserveStockAsync(StockReservationRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await httpClient.PostAsJsonAsync("/api/books/reserve-stock", request, cancellationToken);
                
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<StockReservationResponse>(cancellationToken: cancellationToken) 
                           ?? new StockReservationResponse(false, "Unknown error occurred");
                }
                
                logger.LogWarning("Stock reservation failed with status code {StatusCode}", response.StatusCode);
                return new StockReservationResponse(false, $"Failed with status code: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reserving stock for book {BookId}", request.BookId);
                throw;
            }
        }

        public async Task<bool> ValidateOrderItemsStockAsync(IEnumerable<OrderItem> items)
        {
            try
            {
                foreach (var item in items)
                {
                    var validationResult = await ValidateStockAsync(
                        new StockValidationRequest(item.BookId, item.Quantity));
                    
                    if (!validationResult.IsAvailable)
                    {
                        logger.LogWarning("Insufficient stock for book {BookId}. Requested: {Quantity}, Available: {CurrentStock}", 
                            item.BookId, item.Quantity, validationResult.CurrentStock);
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error validating order items stock");
                return false;
            }
        }

        public async Task<bool> ReserveOrderItemsStockAsync(IEnumerable<OrderItem> items, Guid orderId)
        {
            try
            {
                foreach (var item in items)
                {
                    var reservationResult = await ReserveStockAsync(
                        new StockReservationRequest(item.BookId, item.Quantity, orderId));
                    
                    if (!reservationResult.Success)
                    {
                        logger.LogWarning("Failed to reserve stock for book {BookId}. Reason: {Message}", 
                            item.BookId, reservationResult.Message);
                        return false;
                    }
                }
                
                return true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error reserving order items stock for order {OrderId}", orderId);
                return false;
            }
        }
    }
}