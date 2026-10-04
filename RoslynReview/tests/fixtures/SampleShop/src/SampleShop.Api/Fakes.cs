using SampleShop.Orders;

namespace SampleShop.Api;

internal sealed class InMemoryInventory : IInventory
{
    public Task<bool> TryReserveAsync(string sku, int quantity, CancellationToken cancellationToken) =>
        Task.FromResult(quantity <= 100);
}

internal sealed class FakePaymentGateway : IPaymentGateway
{
    public Task<bool> ChargeAsync(string customerId, decimal amount, CancellationToken cancellationToken) =>
        Task.FromResult(amount < 10_000m);
}
