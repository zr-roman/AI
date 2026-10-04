namespace SampleShop.Orders;

public sealed record OrderLine(string Sku, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;
}

public sealed record Order(Guid Id, string CustomerId, IReadOnlyList<OrderLine> Lines)
{
    public decimal Subtotal => Lines.Sum(line => line.Total);
}

public sealed record OrderResult(Guid OrderId, bool Succeeded, string? Error = null);
