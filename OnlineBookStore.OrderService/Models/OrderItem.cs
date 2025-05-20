using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineBookstore.OrderService.Models;

public sealed class OrderItem
{
    [Key]
    public Guid Id { get; set; }

    [ForeignKey(nameof(Order))]
    public Guid OrderId { get; set; }
    public Order Order { get; set; }

    // references CatalogService.Book.Id
    public Guid BookId { get; set; }

    public int Quantity { get; set; }

    [Column(TypeName = "numeric(10,2)")]
    public decimal UnitPrice { get; set; }
}