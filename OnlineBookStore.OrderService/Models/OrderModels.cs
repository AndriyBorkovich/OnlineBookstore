using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineBookstore.OrderService.Models
{
    [Table("Customers")]
    public class Customer
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        [Required, EmailAddress]
        public string Email { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        // Navigation
        public ICollection<Order> Orders { get; set; } = new List<Order>();
    }

    [Table("Orders")]
    public class Order
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
        public ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    }

    [Table("OrderItems")]
    public class OrderItem
    {
        [Key]
        public Guid Id { get; set; }

        [ForeignKey(nameof(Order))]
        public Guid OrderId { get; set; }
        public Order? Order { get; set; }

        // references CatalogService.Book.Id
        public Guid BookId { get; set; }

        public int Quantity { get; set; }

        [Column(TypeName = "numeric(10,2)")]
        public decimal UnitPrice { get; set; }
    }
}