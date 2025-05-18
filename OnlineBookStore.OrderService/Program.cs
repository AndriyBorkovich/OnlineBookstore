using Microsoft.EntityFrameworkCore;
using OnlineBookstore.OrderService.Data;
using OnlineBookstore.OrderService.Models;
using OnlineBookstore.OrderService.Services;
using Scalar.AspNetCore;
using System.Net;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container
builder.Services.AddOpenApi();
builder.Services.AddEndpointsApiExplorer();

builder.AddNpgsqlDbContext<OrderDbContext>("orderdb");

// Configure CatalogService client with resilience
var httpClientBuilder = builder.Services
    .AddHttpClient<CatalogServiceClient>(client =>
    {
        // This uses Aspire service discovery to locate the catalog service
        client.BaseAddress = new Uri("https+http://catalogservice");
    });

// Add resilience
httpClientBuilder.AddStandardResilienceHandler(options =>
{
    // Configure retry policy
    options.Retry.MaxRetryAttempts = 5;
    options.Retry.Delay = TimeSpan.FromSeconds(1);
    options.Retry.UseJitter = true;
    
    // Add custom retry predicate for specific status codes
    options.Retry.ShouldHandle = args => 
        ValueTask.FromResult(args.Outcome.Result?.StatusCode is 
            HttpStatusCode.ServiceUnavailable or 
            HttpStatusCode.TooManyRequests or 
            HttpStatusCode.InternalServerError);
    
    // Configure circuit breaker
    options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
    options.CircuitBreaker.FailureRatio = 0.5;
    options.CircuitBreaker.MinimumThroughput = 10;
    options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
});

// Add service discovery separately
httpClientBuilder.AddServiceDiscovery();

var app = builder.Build();

app.MapDefaultEndpoints();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(opt =>
    {
        opt.Title = "Order API";
        opt.Theme = ScalarTheme.Kepler;
        opt.DefaultHttpClient = new(ScalarTarget.Http, ScalarClient.Http11);
        opt.OperationSorter = OperationSorter.Alpha;
    });

    // Apply migrations in development
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    dbContext.Database.Migrate();
}

app.UseHttpsRedirection();

// Define API groups
var customersApi = app.MapGroup("/api/customers");
var ordersApi = app.MapGroup("/api/orders");

// Customers endpoints
customersApi.MapGet("/", async (OrderDbContext db) => 
    await db.Customers.ToListAsync());

customersApi.MapGet("/{id:guid}", async (Guid id, OrderDbContext db) =>
{
    var customer = await db.Customers.FindAsync(id);
    return customer is null ? Results.NotFound() : Results.Ok(customer);
});

customersApi.MapGet("/{id:guid}/orders", async (Guid id, OrderDbContext db) => 
    await db.Orders
        .Include(o => o.Items)
        .Where(o => o.CustomerId == id)
        .ToListAsync());

customersApi.MapPost("/", async (Customer customer, OrderDbContext db) =>
{
    db.Customers.Add(customer);
    await db.SaveChangesAsync();
    return Results.Created($"/api/customers/{customer.Id}", customer);
});

customersApi.MapPut("/{id:guid}", async (Guid id, Customer customer, OrderDbContext db) =>
{
    if (id != customer.Id)
    {
        return Results.BadRequest();
    }

    db.Entry(customer).State = EntityState.Modified;

    try
    {
        await db.SaveChangesAsync();
    }
    catch (DbUpdateConcurrencyException)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == id))
        {
            return Results.NotFound();
        }
        throw;
    }

    return Results.NoContent();
});

customersApi.MapDelete("/{id:guid}", async (Guid id, OrderDbContext db) =>
{
    var customer = await db.Customers.FindAsync(id);
    if (customer is null)
    {
        return Results.NotFound();
    }

    db.Customers.Remove(customer);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

// Orders endpoints
ordersApi.MapGet("/", async (OrderDbContext db) => 
    await db.Orders
        .Include(o => o.Customer)
        .Include(o => o.Items)
        .ToListAsync());

ordersApi.MapGet("/{id:guid}", async (Guid id, OrderDbContext db) =>
{
    var order = await db.Orders
        .Include(o => o.Customer)
        .Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == id);

    return order is null ? Results.NotFound() : Results.Ok(order);
});

ordersApi.MapPost("/", async (Order order, OrderDbContext db, CatalogServiceClient catalogClient, ILogger<Program> logger) =>
{
    try
    {
        // Validate stock for all items before proceeding
        var stockValidationResult = await catalogClient.ValidateOrderItemsStockAsync(order.Items);
        if (!stockValidationResult)
        {
            return Results.BadRequest("Cannot create order: One or more items are not in stock in the requested quantity");
        }

        // Calculate total amount
        if (order.Items?.Count > 0)
        {
            order.TotalAmount = order.Items.Sum(item => item.UnitPrice * item.Quantity);
        }

        // Save order to generate ID
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Reserve stock for all items
        var stockReservationResult = await catalogClient.ReserveOrderItemsStockAsync(order.Items, order.Id);
        if (!stockReservationResult)
        {
            // Cleanup the order if stock reservation failed
            db.Orders.Remove(order);
            await db.SaveChangesAsync();
            
            return Results.BadRequest("Failed to reserve stock for one or more items");
        }

        logger.LogInformation("Order {OrderId} created successfully with {ItemCount} items, total amount: {TotalAmount}", 
            order.Id, order.Items.Count, order.TotalAmount);
            
        return Results.Created($"/api/orders/{order.Id}", order);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error creating order");
        return Results.StatusCode(500);
    }
});

ordersApi.MapPut("/{id:guid}", async (Guid id, Order order, OrderDbContext db) =>
{
    if (id != order.Id)
    {
        return Results.BadRequest();
    }
    if (!await db.Orders.AnyAsync(o => o.Id == id))
    {
        return Results.NotFound();
    }

    db.Entry(order).State = EntityState.Modified;
    
    await db.SaveChangesAsync();

    return Results.Ok("edited");
});

ordersApi.MapPut("/{id:guid}/status", async (Guid id, string status, OrderDbContext db, CatalogServiceClient catalogClient, ILogger<Program> logger) =>
{
    var order = await db.Orders
        .Include(o => o.Items)
        .FirstOrDefaultAsync(o => o.Id == id);
        
    if (order is null)
    {
        return Results.NotFound("Order not found");
    }

    var validStatuses = new[] { "Pending", "Paid", "Shipped", "Cancelled" };
    if (!validStatuses.Contains(status))
    {
        return Results.BadRequest($"Invalid status. Valid statuses are: {string.Join(", ", validStatuses)}");
    }

    var oldStatus = order.Status;
    order.Status = status;
    
    // Handle stock implications based on status change
    try
    {
        if (status == "Paid" && oldStatus == "Pending")
        {
            // When changing from Pending to Paid, commit the stock reservation
            bool allCommitted = true;
            foreach (var item in order.Items)
            {
                try
                {
                    var response = await catalogClient.ReserveStockAsync(
                        new CatalogServiceClient.StockReservationRequest(item.BookId, item.Quantity, order.Id));
                        
                    if (!response.Success)
                    {
                        logger.LogWarning("Failed to commit stock for item {ItemId} in order {OrderId}", item.Id, order.Id);
                        allCommitted = false;
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error committing stock for item {ItemId} in order {OrderId}", item.Id, order.Id);
                    allCommitted = false;
                }
            }
            
            if (!allCommitted)
            {
                return Results.BadRequest("Failed to update stock levels for one or more items");
            }
        }
        else if (status == "Cancelled" && oldStatus != "Cancelled")
        {
            // When cancelling an order, release any reserved stock
            logger.LogInformation("Cancelling order {OrderId}, releasing reserved stock", order.Id);
            // Note: No need to make API calls here as the reservation will expire
        }
        
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error updating order status");
        return Results.StatusCode(500);
    }
});

ordersApi.MapDelete("/{id:guid}", async (Guid id, OrderDbContext db) =>
{
    var order = await db.Orders.FindAsync(id);
    if (order is null)
    {
        return Results.NotFound();
    }

    db.Orders.Remove(order);
    await db.SaveChangesAsync();
    
    return Results.NoContent();
});

app.Run();
