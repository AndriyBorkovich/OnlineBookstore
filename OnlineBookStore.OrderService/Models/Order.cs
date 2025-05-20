using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineBookstore.OrderService.Models;

public sealed class Order
{
    [Key]
    public Guid Id { get; set; }

    [ForeignKey(nameof(Customer))]
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    public decimal TotalAmount { get; set; }

    [Required]
    public string Status { get; set; } = "Pending";  // "Pending","Paid","Shipped","Cancelled"

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    // Navigation
    public ICollection<OrderItem> Items { get; set; } = [];
}
