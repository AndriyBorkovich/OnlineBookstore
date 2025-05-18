using System.ComponentModel.DataAnnotations;

namespace OnlineBookstore.CatalogService.Models;

public sealed class Category
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<Book> Books { get; set; } = new List<Book>();
}