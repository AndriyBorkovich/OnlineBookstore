using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using OnlineBookstore.ReviewService.Models;

namespace OnlineBookstore.ReviewService.Services
{
    public class ReviewCacheService
    {
        private readonly IDistributedCache _cache;
        private readonly DistributedCacheEntryOptions _cacheOptions;
        private readonly ILogger<ReviewCacheService> _logger;

        public ReviewCacheService(IDistributedCache cache, ILogger<ReviewCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
            
            // Set cache to expire after 15 minutes by default
            _cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15)
            };
        }

        private string GetRecentReviewsKey() => "recent_reviews";
        private string GetBookReviewsKey(Guid bookId) => $"book:{bookId}:reviews";
        private string GetBookRatingKey(Guid bookId) => $"book:{bookId}:rating";

        public async Task CacheRecentReviewsAsync(List<Review> reviews)
        {
            try
            {
                var key = GetRecentReviewsKey();
                var reviewsJson = JsonSerializer.Serialize(reviews);
                await _cache.SetStringAsync(key, reviewsJson, _cacheOptions);
                _logger.LogInformation("Recent reviews cached successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching recent reviews");
            }
        }

        public async Task<List<Review>?> GetRecentReviewsAsync()
        {
            try
            {
                var key = GetRecentReviewsKey();
                var reviewsJson = await _cache.GetStringAsync(key);
                
                if (string.IsNullOrEmpty(reviewsJson))
                    return null;
                    
                return JsonSerializer.Deserialize<List<Review>>(reviewsJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving recent reviews from cache");
                return null;
            }
        }

        public async Task CacheBookReviewsAsync(Guid bookId, List<Review> reviews)
        {
            try
            {
                var key = GetBookReviewsKey(bookId);
                var reviewsJson = JsonSerializer.Serialize(reviews);
                await _cache.SetStringAsync(key, reviewsJson, _cacheOptions);
                _logger.LogInformation("Reviews for book {BookId} cached successfully", bookId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching reviews for book {BookId}", bookId);
            }
        }

        public async Task<List<Review>?> GetBookReviewsAsync(Guid bookId)
        {
            try
            {
                var key = GetBookReviewsKey(bookId);
                var reviewsJson = await _cache.GetStringAsync(key);
                
                if (string.IsNullOrEmpty(reviewsJson))
                    return null;
                    
                return JsonSerializer.Deserialize<List<Review>>(reviewsJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving reviews for book {BookId} from cache", bookId);
                return null;
            }
        }

        public async Task CacheBookRatingAsync(Guid bookId, double rating)
        {
            try
            {
                var key = GetBookRatingKey(bookId);
                await _cache.SetStringAsync(key, rating.ToString(), _cacheOptions);
                _logger.LogInformation("Rating for book {BookId} cached successfully", bookId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching rating for book {BookId}", bookId);
            }
        }

        public async Task<double?> GetBookRatingAsync(Guid bookId)
        {
            try
            {
                var key = GetBookRatingKey(bookId);
                var ratingStr = await _cache.GetStringAsync(key);
                
                if (string.IsNullOrEmpty(ratingStr))
                    return null;
                    
                return double.Parse(ratingStr);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving rating for book {BookId} from cache", bookId);
                return null;
            }
        }

        public async Task InvalidateBookCacheAsync(Guid bookId)
        {
            try
            {
                await _cache.RemoveAsync(GetBookReviewsKey(bookId));
                await _cache.RemoveAsync(GetBookRatingKey(bookId));
                await _cache.RemoveAsync(GetRecentReviewsKey());
                _logger.LogInformation("Cache for book {BookId} invalidated", bookId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invalidating cache for book {BookId}", bookId);
            }
        }
    }
}