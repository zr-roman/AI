namespace SampleShop.Pricing;

public static class PriceCalculator
{
    public const decimal LoyaltyDiscountRate = 0.05m;

    public static decimal ApplyDiscount(decimal amount, decimal rate)
    {
        if (rate is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(rate));
        }

        return Math.Round(amount * (1 - rate), 2);
    }
}
