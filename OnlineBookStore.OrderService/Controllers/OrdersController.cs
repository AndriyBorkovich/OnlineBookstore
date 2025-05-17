using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineBookstore.OrderService.Data;
using OnlineBookstore.OrderService.Models;

namespace OnlineBookstore.OrderService.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly OrderDbContext _dbContext;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(OrderDbContext dbContext, ILogger<OrdersController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<Order>>> GetOrders()
        {
            return await _dbContext.Orders
                .Include(o => o.Customer)
                .Include(o => o.Items)
                .ToListAsync();
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<Order>> GetOrder(Guid id)
        {
            var order = await _dbContext.Orders
                .Include(o => o.Customer)
                .Include(o => o.Items)
                .FirstOrDefaultAsync(o => o.Id == id);

            if (order == null)
            {
                return NotFound();
            }

            return order;
        }

        [HttpPost]
        public async Task<ActionResult<Order>> CreateOrder(Order order)
        {
            // Calculate total amount
            if (order.Items?.Count > 0)
            {
                order.TotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);
            }

            _dbContext.Orders.Add(order);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateOrder(Guid id, Order order)
        {
            if (id != order.Id)
            {
                return BadRequest();
            }

            _dbContext.Entry(order).State = EntityState.Modified;

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await OrderExists(id))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return NoContent();
        }

        [HttpPut("{id:guid}/status")]
        public async Task<IActionResult> UpdateOrderStatus(Guid id, [FromBody] string status)
        {
            var order = await _dbContext.Orders.FindAsync(id);
            if (order == null)
            {
                return NotFound();
            }

            // Validate status
            var validStatuses = new[] { "Pending", "Paid", "Shipped", "Cancelled" };
            if (!validStatuses.Contains(status))
            {
                return BadRequest("Invalid status. Valid statuses are: Pending, Paid, Shipped, Cancelled");
            }

            order.Status = status;
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteOrder(Guid id)
        {
            var order = await _dbContext.Orders.FindAsync(id);
            if (order == null)
            {
                return NotFound();
            }

            _dbContext.Orders.Remove(order);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        private async Task<bool> OrderExists(Guid id)
        {
            return await _dbContext.Orders.AnyAsync(e => e.Id == id);
        }
    }
}