using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineBookstore.CatalogService.Models
{
    [Table("Books")]
    public class Book
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string Title { get; set; } = string.Empty;

        [Required]
        public string Author { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        [ForeignKey(nameof(Category))]
        public Guid? CategoryId { get; set; }
        public Category? Category { get; set; }

        [Column(TypeName = "numeric(10,2)")]
        public decimal Price { get; set; }

        public int Stock { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        // Field for Elasticsearch vector embeddings
        [NotMapped]
        public float[]? VectorEmbedding { get; set; }
    }
}