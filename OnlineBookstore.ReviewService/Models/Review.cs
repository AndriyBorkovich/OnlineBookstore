using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace OnlineBookstore.ReviewService.Models
{
    public sealed class Review
    {
        [BsonId]
        public ObjectId Id { get; set; }

        [BsonElement("BookId")]
        public Guid BookId { get; set; }

        [BsonElement("UserId")]
        public Guid UserId { get; set; }

        [BsonElement("Rating")]
        public int Rating { get; set; }  // 1–5

        [BsonElement("Comment")]
        public string Comment { get; set; } = string.Empty;

        [BsonElement("CreatedAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}