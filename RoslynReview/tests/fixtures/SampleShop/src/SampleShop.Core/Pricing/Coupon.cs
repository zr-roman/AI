namespace SampleShop.Pricing;

public sealed record Coupon(string Code, decimal Rate)
{
    public bool IsValid => Rate is > 0 and <= 0.5m;
}
