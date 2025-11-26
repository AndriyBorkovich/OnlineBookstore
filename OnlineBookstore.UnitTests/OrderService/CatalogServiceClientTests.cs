using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using OnlineBookstore.OrderService.Models;
using OnlineBookstore.OrderService.Services;

namespace OnlineBookstore.UnitTests.OrderService;

public sealed class CatalogServiceClientTests
{
    private readonly Mock<ILogger<CatalogServiceClient>> _loggerMock = new();
    private readonly Mock<HttpMessageHandler> _handlerMock = new(MockBehavior.Strict);
    private readonly HttpClient _httpClient;
    private readonly CatalogServiceClient _client;

    public CatalogServiceClientTests()
    {
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("https://catalog.test")
        };
        _client = new CatalogServiceClient(_httpClient, _loggerMock.Object);
    }

    private void SetupHttpResponse(string expectedPath, HttpStatusCode status, string content)
    {
        _handlerMock
           .Protected()
           .Setup<Task<HttpResponseMessage>>(
               "SendAsync",
               ItExpr.Is<HttpRequestMessage>(req =>
                   req.RequestUri!.AbsolutePath == expectedPath),
               ItExpr.IsAny<CancellationToken>()
           )
           .ReturnsAsync(new HttpResponseMessage
           {
               StatusCode = status,
               Content = new StringContent(content, Encoding.UTF8, "application/json")
           })
           .Verifiable();
    }

    [Fact]
    public async Task GetBookStockInfoAsync_ReturnsBookStockInfo_OnSuccess()
    {
        var bookId = Guid.NewGuid();
        var dto = new CatalogServiceClient.BookStockInfo(bookId, "Title", 5);
        var json = JsonSerializer.Serialize(dto);

        SetupHttpResponse($"/api/books/{bookId}/stock", HttpStatusCode.OK, json);

        var result = await _client.GetBookStockInfoAsync(bookId);

        Assert.NotNull(result);
        Assert.Equal(dto.Id, result!.Id);
        Assert.Equal(dto.Title, result.Title);
        Assert.Equal(dto.Stock, result.Stock);
        _handlerMock.Protected().Verify("SendAsync", Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get &&
                r.RequestUri!.AbsolutePath == $"/api/books/{bookId}/stock"),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task GetBookStockInfoAsync_ReturnsNull_OnNotFound()
    {
        var bookId = Guid.NewGuid();
        SetupHttpResponse($"/api/books/{bookId}/stock", HttpStatusCode.NotFound, "");

        var result = await _client.GetBookStockInfoAsync(bookId);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidateStockAsync_ReturnsTrue_WhenAvailable()
    {
        var req = new CatalogServiceClient.StockValidationRequest(Guid.NewGuid(), 3);
        var respDto = new CatalogServiceClient.StockValidationResponse(true, 10);
        var json = JsonSerializer.Serialize(respDto);

        SetupHttpResponse("/api/books/validate-stock", HttpStatusCode.OK, json);

        var result = await _client.ValidateStockAsync(req);

        Assert.True(result.IsAvailable);
        Assert.Equal(10, result.CurrentStock);
    }

    [Fact]
    public async Task ValidateStockAsync_ReturnsFalse_OnBadStatus()
    {
        var req = new CatalogServiceClient.StockValidationRequest(Guid.NewGuid(), 1);
        SetupHttpResponse("/api/books/validate-stock", HttpStatusCode.BadRequest, "");

        var result = await _client.ValidateStockAsync(req);

        Assert.False(result.IsAvailable);
        Assert.Equal(0, result.CurrentStock);
    }

    [Fact]
    public async Task ReserveStockAsync_ReturnsSuccess_WhenReserved()
    {
        var req = new CatalogServiceClient.StockReservationRequest(Guid.NewGuid(), 2, Guid.NewGuid());
        var respDto = new CatalogServiceClient.StockReservationResponse(true, "OK");
        var json = JsonSerializer.Serialize(respDto);

        SetupHttpResponse("/api/books/reserve-stock", HttpStatusCode.OK, json);

        var result = await _client.ReserveStockAsync(req);

        Assert.True(result.Success);
        Assert.Equal("OK", result.Message);
    }

    [Fact]
    public async Task ReserveStockAsync_ReturnsFailure_OnBadStatus()
    {
        var req = new CatalogServiceClient.StockReservationRequest(Guid.NewGuid(), 2, Guid.NewGuid());
        SetupHttpResponse("/api/books/reserve-stock", HttpStatusCode.InternalServerError, "");

        var result = await _client.ReserveStockAsync(req);

        Assert.False(result.Success);
        Assert.Contains("Failed with status code", result.Message);
    }

    [Fact]
    public async Task ValidateOrderItemsStockAsync_ReturnsFalse_IfAnyUnavailable()
    {
        var item1 = new OrderItem { BookId = Guid.NewGuid(), Quantity = 1 };
        var item2 = new OrderItem { BookId = Guid.NewGuid(), Quantity = 2 };

        // First item available
        var okDto = new CatalogServiceClient.StockValidationResponse(true, 5);
        // Second unavailable
        var notOkDto = new CatalogServiceClient.StockValidationResponse(false, 0);

        // Setup first call
        _handlerMock
           .Protected()
           .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
               ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
           .ReturnsAsync(new HttpResponseMessage
           {
               StatusCode = HttpStatusCode.OK,
               Content = new StringContent(JsonSerializer.Serialize(okDto), Encoding.UTF8, "application/json")
           })
           .ReturnsAsync(new HttpResponseMessage
           {
               StatusCode = HttpStatusCode.OK,
               Content = new StringContent(JsonSerializer.Serialize(notOkDto), Encoding.UTF8, "application/json")
           });

        var result = await _client.ValidateOrderItemsStockAsync([item1, item2]);

        Assert.False(result);
    }

    [Fact]
    public async Task ValidateOrderItemsStockAsync_ReturnsFalse_WhenAllAvailable()
    {
        var item = new OrderItem { BookId = Guid.NewGuid(), Quantity = 3 };
        var okDto = new CatalogServiceClient.StockValidationResponse(true, 10);

        SetupHttpResponse("/api/books/validate-stock", HttpStatusCode.OK,
            JsonSerializer.Serialize(okDto));

        var result = await _client.ValidateOrderItemsStockAsync([item, item]);

        Assert.False(result);
    }

    [Fact]
    public async Task ReserveOrderItemsStockAsync_ReturnsFalse_IfAnyFails()
    {
        var item1 = new OrderItem { BookId = Guid.NewGuid(), Quantity = 1 };
        var item2 = new OrderItem { BookId = Guid.NewGuid(), Quantity = 1 };
        var orderId = Guid.NewGuid();

        var okDto = new CatalogServiceClient.StockReservationResponse(true, "OK");
        var failDto = new CatalogServiceClient.StockReservationResponse(false, "Sold out");

        _handlerMock
           .Protected()
           .SetupSequence<Task<HttpResponseMessage>>("SendAsync",
               ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
           .ReturnsAsync(new HttpResponseMessage
           {
               StatusCode = HttpStatusCode.OK,
               Content = new StringContent(JsonSerializer.Serialize(okDto), Encoding.UTF8, "application/json")
           })
           .ReturnsAsync(new HttpResponseMessage
           {
               StatusCode = HttpStatusCode.OK,
               Content = new StringContent(JsonSerializer.Serialize(failDto), Encoding.UTF8, "application/json")
           });

        var result = await _client.ReserveOrderItemsStockAsync(new[] { item1, item2 }, orderId);

        Assert.False(result);
    }

    [Fact]
    public async Task ReserveOrderItemsStockAsync_ReturnsFalse_WhenAllSucceed()
    {
        var item = new OrderItem { BookId = Guid.NewGuid(), Quantity = 1 };
        var orderId = Guid.NewGuid();
        var okDto = new CatalogServiceClient.StockReservationResponse(true, "OK");

        SetupHttpResponse("/api/books/reserve-stock", HttpStatusCode.OK,
            JsonSerializer.Serialize(okDto));

        var result = await _client.ReserveOrderItemsStockAsync(new[] { item, item }, orderId);

        Assert.False(result);
    }
}
