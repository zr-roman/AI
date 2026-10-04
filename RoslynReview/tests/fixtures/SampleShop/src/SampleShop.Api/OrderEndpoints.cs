using SampleShop.Orders;

namespace SampleShop.Api;

public sealed class OrderEndpoints(IOrderService orders)
{
    public async Task<string> CreateOrderAsync(string customerId, IReadOnlyList<OrderLine> lines, CancellationToken cancellationToken)
    {
        var order = new Order(Guid.NewGuid(), customerId, lines);
        var result = await orders.PlaceOrderAsync(order, cancellationToken);
        return result.Succeeded ? $"created {result.OrderId}" : $"failed: {result.Error}";
    }

    public decimal PreviewDiscount(IReadOnlyList<OrderLine> lines) =>
        OrderService.CalculateDiscountRate(new Order(Guid.Empty, "preview", lines));
}
