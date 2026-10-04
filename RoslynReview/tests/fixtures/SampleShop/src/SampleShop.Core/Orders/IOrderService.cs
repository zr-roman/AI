namespace SampleShop.Orders;

public interface IOrderService
{
    Task<OrderResult> PlaceOrderAsync(Order order, CancellationToken cancellationToken);
}

public interface IInventory
{
    Task<bool> TryReserveAsync(string sku, int quantity, CancellationToken cancellationToken);
}

public interface IPaymentGateway
{
    Task<bool> ChargeAsync(string customerId, decimal amount, CancellationToken cancellationToken);
}
