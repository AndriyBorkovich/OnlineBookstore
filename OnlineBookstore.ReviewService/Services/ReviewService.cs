using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using OnlineBookstore.ReviewService.Models;

namespace OnlineBookstore.ReviewService.Services
{
    public class ReviewService
    {
        private readonly IMongoCollection<Review> _reviewsCollection;
        private readonly ILogger<ReviewService> _logger;

        public ReviewService(
            IMongoClient mongoClient,
            ILogger<ReviewService> logger)
        {
            var database = mongoClient.GetDatabase("reviewsdb");
            _reviewsCollection = database.GetCollection<Review>("reviews");
            _logger = logger;
            
            // Create indexes
            CreateIndexes();
        }

        private void CreateIndexes()
        {
            try
            {
                var indexKeys = Builders<Review>.IndexKeys.Ascending(r => r.BookId);
                var indexOptions = new CreateIndexOptions { Name = "BookId_Index" };
                var indexModel = new CreateIndexModel<Review>(indexKeys, indexOptions);
                _reviewsCollection.Indexes.CreateOne(indexModel);

                var userIndexKeys = Builders<Review>.IndexKeys.Ascending(r => r.UserId);
                var userIndexOptions = new CreateIndexOptions { Name = "UserId_Index" };
                var userIndexModel = new CreateIndexModel<Review>(userIndexKeys, userIndexOptions);
                _reviewsCollection.Indexes.CreateOne(userIndexModel);

                var dateIndexKeys = Builders<Review>.IndexKeys.Descending(r => r.CreatedAt);
                var dateIndexOptions = new CreateIndexOptions { Name = "CreatedAt_Index" };
                var dateIndexModel = new CreateIndexModel<Review>(dateIndexKeys, dateIndexOptions);
                _reviewsCollection.Indexes.CreateOne(dateIndexModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating indexes");
            }
        }

        public async Task<List<Review>> GetReviewsAsync()
        {
            return await _reviewsCollection
                .Find(_ => true)
                .SortByDescending(r => r.CreatedAt)
                .Limit(50)
                .ToListAsync();
        }

        public async Task<Review> GetReviewAsync(string id)
        {
            return await _reviewsCollection
                .Find(r => r.Id == ObjectId.Parse(id))
                .FirstOrDefaultAsync();
        }

        public async Task<List<Review>> GetReviewsByBookIdAsync(Guid bookId)
        {
            return await _reviewsCollection
                .Find(r => r.BookId == bookId)
                .SortByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<List<Review>> GetReviewsByUserIdAsync(Guid userId)
        {
            return await _reviewsCollection
                .Find(r => r.UserId == userId)
                .SortByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<Review> CreateReviewAsync(Review review)
        {
            await _reviewsCollection.InsertOneAsync(review);
            return review;
        }

        public async Task UpdateReviewAsync(string id, Review reviewIn)
        {
            await _reviewsCollection.ReplaceOneAsync(
                r => r.Id == ObjectId.Parse(id),
                reviewIn);
        }

        public async Task DeleteReviewAsync(string id)
        {
            await _reviewsCollection.DeleteOneAsync(r => r.Id == ObjectId.Parse(id));
        }

        public async Task<double> GetAverageRatingForBookAsync(Guid bookId)
        {
            var filter = Builders<Review>.Filter.Eq(r => r.BookId, bookId);
            var reviews = await _reviewsCollection.Find(filter).ToListAsync();
            
            if (reviews.Count == 0)
                return 0;
                
            return reviews.Average(r => r.Rating);
        }
    }
}