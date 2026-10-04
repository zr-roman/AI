using SampleShop.Pricing;

namespace SampleShop.Orders;

/// <summary>Places orders: reserves stock, applies discounts and charges the customer.</summary>
public sealed class OrderService(IInventory inventory, IPaymentGateway payments) : IOrderService
{
    private readonly IInventory _inventory = inventory;
    private readonly IPaymentGateway _payments = payments;

    public int MaxLinesPerOrder { get; init; } = 50;

    /// <summary>Places the order if every line can be reserved and the payment succeeds.</summary>
    public async Task<OrderResult> PlaceOrderAsync(Order order, CancellationToken cancellationToken)
    {
        var validationError = Validate(order);
        if (validationError is not null)
        {
            return new OrderResult(order.Id, Succeeded: false, validationError);
        }

        foreach (var line in order.Lines)
        {
            if (!await _inventory.TryReserveAsync(line.Sku, line.Quantity, cancellationToken))
            {
                return new OrderResult(order.Id, Succeeded: false, $"Out of stock: {line.Sku}");
            }
        }

        var amount = PriceCalculator.ApplyDiscount(order.Subtotal, CalculateDiscountRate(order));
        var charged = await _payments.ChargeAsync(order.CustomerId, amount, cancellationToken);
        return new OrderResult(order.Id, charged, charged ? null : "Payment declined");
    }

    public static decimal CalculateDiscountRate(Order order) =>
        order.Lines.Count >= 10 ? PriceCalculator.LoyaltyDiscountRate : 0m;

    private string? Validate(Order order)
    {
        if (order.Lines.Count == 0)
        {
            return "Order has no lines";
        }

        return order.Lines.Count > MaxLinesPerOrder ? "Too many lines" : null;
    }
}
