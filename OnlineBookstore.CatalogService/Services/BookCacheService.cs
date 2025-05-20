using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using OnlineBookstore.CatalogService.Models;

namespace OnlineBookstore.CatalogService.Services
{
    public class BookCacheService
    {
        private readonly IDistributedCache _cache;
        private readonly DistributedCacheEntryOptions _cacheOptions;
        private readonly ILogger<BookCacheService> _logger;

        public BookCacheService(IDistributedCache cache, ILogger<BookCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
            
            // Set cache to expire after 15 minutes by default
            _cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            };
        }

        private string GetBookKey(Guid id) => $"book:{id}";
        private string GetPopularBooksKey() => "popular_books";

        public async Task CacheBookAsync(Book book)
        {
            try
            {
                var key = GetBookKey(book.Id);
                var bookJson = JsonSerializer.Serialize(book);
                await _cache.SetStringAsync(key, bookJson, _cacheOptions);
                _logger.LogInformation("Book {BookId} cached successfully", book.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching book {BookId}", book.Id);
            }
        }

        public async Task<Book?> GetBookAsync(Guid id)
        {
            try
            {
                var key = GetBookKey(id);
                var bookJson = await _cache.GetStringAsync(key);
                
                if (string.IsNullOrEmpty(bookJson))
                    return null;
                    
                return JsonSerializer.Deserialize<Book>(bookJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving book {BookId} from cache", id);
                return null;
            }
        }

        public async Task CachePopularBooksAsync(List<Book> books)
        {
            try
            {
                var key = GetPopularBooksKey();
                var booksJson = JsonSerializer.Serialize(books);
                await _cache.SetStringAsync(key, booksJson, _cacheOptions);
                _logger.LogInformation("Popular books cached successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching popular books");
            }
        }

        public async Task<List<Book>?> GetPopularBooksAsync()
        {
            try
            {
                var key = GetPopularBooksKey();
                var booksJson = await _cache.GetStringAsync(key);
                
                if (string.IsNullOrEmpty(booksJson))
                    return null;
                    
                return JsonSerializer.Deserialize<List<Book>>(booksJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving popular books from cache");
                return null;
            }
        }

        public async Task RemoveBookFromCacheAsync(Guid id)
        {
            try
            {
                var key = GetBookKey(id);
                await _cache.RemoveAsync(key);
                _logger.LogInformation("Book {BookId} removed from cache", id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing book {BookId} from cache", id);
            }
        }
    }
}