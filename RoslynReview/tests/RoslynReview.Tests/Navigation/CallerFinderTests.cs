using RoslynReview.Core.Navigation;
using RoslynReview.Tests.Infrastructure;

namespace RoslynReview.Tests.Navigation;

[Collection(SampleShopCollection.Name)]
public sealed class CallerFinderTests(SampleShopFixture fixture)
{
    [Fact]
    public async Task Calls_through_an_interface_are_marked_with_via()
    {
        var result = await FindAsync(
            "M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)");

        var caller = Assert.Single(result.Callers);
        Assert.Equal(
            ("OrderEndpoints.CreateOrderAsync(string, IReadOnlyList<OrderLine>, CancellationToken)", "SampleShop.Api"),
            (caller.Name, caller.Project));
        var site = Assert.Single(caller.Sites);
        Assert.Equal(("src/SampleShop.Api/OrderEndpoints.cs", 10), (site.File, site.Line));
        Assert.Equal("var result = await orders.PlaceOrderAsync(order, cancellationToken);", site.Code);
        Assert.Equal("IOrderService.PlaceOrderAsync(Order, CancellationToken)", site.Via);
    }

    [Fact]
    public async Task Direct_callers_are_found_across_projects()
    {
        var result = await FindAsync("M:SampleShop.Orders.OrderService.CalculateDiscountRate(SampleShop.Orders.Order)");

        Assert.Equal(
            new[]
            {
                ("OrderEndpoints.PreviewDiscount(IReadOnlyList<OrderLine>)", "src/SampleShop.Api/OrderEndpoints.cs", 15),
                ("OrderService.PlaceOrderAsync(Order, CancellationToken)", "src/SampleShop.Core/Orders/OrderService.cs", 30),
            },
            result.Callers.Select(caller => (caller.Name, caller.Sites[0].File, caller.Sites[0].Line)));
        Assert.All(result.Callers, caller => Assert.Null(caller.Sites[0].Via));
    }

    [Fact]
    public async Task Constant_reads_are_usages()
    {
        var result = await FindAsync("F:SampleShop.Pricing.PriceCalculator.LoyaltyDiscountRate");

        var caller = Assert.Single(result.Callers);
        Assert.Equal(("OrderService.CalculateDiscountRate(Order)", 36), (caller.Name, Assert.Single(caller.Sites).Line));
    }

    [Fact]
    public async Task Type_usages_are_grouped_by_calling_member()
    {
        var result = await FindAsync("T:SampleShop.Orders.OrderService");

        Assert.Equal(
            new[] { "OrderEndpoints.PreviewDiscount(IReadOnlyList<OrderLine>)", "<top-level statements>" },
            result.Callers.Select(caller => caller.Name));
    }

    [Fact]
    public async Task Usages_in_signatures_belong_to_the_declaring_member()
    {
        var result = await FindAsync("T:SampleShop.Orders.Order");

        Assert.Equal(
            new[]
            {
                "OrderEndpoints.CreateOrderAsync(string, IReadOnlyList<OrderLine>, CancellationToken)",
                "OrderEndpoints.PreviewDiscount(IReadOnlyList<OrderLine>)",
                "IOrderService.PlaceOrderAsync(Order, CancellationToken)",
                "OrderService.PlaceOrderAsync(Order, CancellationToken)",
                "OrderService.CalculateDiscountRate(Order)",
                "OrderService.Validate(Order)",
            },
            result.Callers.Select(caller => caller.Name));
        // `new Order(...)` uses the type itself, not something "via" its constructor.
        Assert.All(result.Callers.SelectMany(caller => caller.Sites), site => Assert.Null(site.Via));
    }

    [Fact]
    public async Task Max_results_limits_the_listed_call_sites()
    {
        var result = await FindAsync("T:SampleShop.Orders.Order", maxResults: 2);

        Assert.True(result.Truncated);
        Assert.True(result.TotalSites > 2, $"expected more than 2 usages of Order, found {result.TotalSites}");
        Assert.Equal(2, result.Callers.Sum(caller => caller.Sites.Count));
    }

    private Task<CallersResult> FindAsync(string symbolId, int maxResults = CallerFinder.DefaultMaxResults) =>
        CallerFinder.FindAsync(fixture.Snapshot, symbolId, maxResults, TestContext.Current.CancellationToken);
}
