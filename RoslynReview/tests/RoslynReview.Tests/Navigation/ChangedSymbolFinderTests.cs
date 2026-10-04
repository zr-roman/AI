using RoslynReview.Core.Navigation;
using RoslynReview.Tests.Infrastructure;

namespace RoslynReview.Tests.Navigation;

[Collection(SampleShopCollection.Name)]
public sealed class ChangedSymbolFinderTests(SampleShopFixture fixture)
{
    private const string OrderServiceFile = "src/SampleShop.Core/Orders/OrderService.cs";

    [Fact]
    public async Task Maps_added_and_modified_members()
    {
        var result = await FindAsync("01-feature.diff");

        Assert.Equal(
            new[]
            {
                ("OrderService.MaxLinesPerOrder", "property", SymbolChange.Added, 11, 11),
                ("OrderService.PlaceOrderAsync(Order, CancellationToken)", "method", SymbolChange.Modified, 14, 33),
                ("OrderService.CalculateDiscountRate(Order)", "method", SymbolChange.Modified, 35, 36),
                ("OrderService.Validate(Order)", "method", SymbolChange.Added, 38, 46),
            },
            result.Symbols
                .Where(symbol => symbol.File == OrderServiceFile)
                .Select(symbol => (symbol.Name, symbol.Kind, symbol.Change, symbol.StartLine, symbol.EndLine)));
    }

    [Fact]
    public async Task Doc_comment_edits_count_toward_the_member_below()
    {
        var result = await FindAsync("01-feature.diff");

        var placeOrder = Assert.Single(result.Symbols, symbol => symbol.Name.StartsWith("OrderService.PlaceOrderAsync", StringComparison.Ordinal));
        Assert.Equal(5, placeOrder.ChangedLines); // the doc comment plus four lines of the body
        Assert.Equal("public", placeOrder.Accessibility);
        Assert.Equal(
            "M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)",
            placeOrder.Id);
    }

    [Fact]
    public async Task A_new_type_is_reported_once_without_its_members()
    {
        var result = await FindAsync("01-feature.diff");

        var coupon = Assert.Single(result.Symbols, symbol => symbol.File == "src/SampleShop.Core/Pricing/Coupon.cs");
        Assert.Equal(("T:SampleShop.Pricing.Coupon", "record", SymbolChange.Added), (coupon.Id, coupon.Kind, coupon.Change));
    }

    [Fact]
    public async Task Constants_and_top_level_statements_are_reported()
    {
        var result = await FindAsync("01-feature.diff");

        var constant = Assert.Single(result.Symbols, symbol => symbol.Name == "PriceCalculator.LoyaltyDiscountRate");
        Assert.Equal(("constant", SymbolChange.Added), (constant.Kind, constant.Change));

        var program = Assert.Single(result.Symbols, symbol => symbol.File == "src/SampleShop.Api/Program.cs");
        Assert.Equal(("<top-level statements>", SymbolChange.Modified), (program.Name, program.Change));
    }

    [Fact]
    public async Task Every_file_in_the_diff_gets_a_status()
    {
        var result = await FindAsync("01-feature.diff");

        Assert.Equal(
            new[]
            {
                ("README.md", FileStatus.NotCSharp),
                ("src/SampleShop.Api/Program.cs", FileStatus.Modified),
                ("src/SampleShop.Core/Legacy/OldPricing.cs", FileStatus.Deleted),
                (OrderServiceFile, FileStatus.Modified),
                ("src/SampleShop.Core/Pricing/Coupon.cs", FileStatus.Added),
                ("src/SampleShop.Core/Pricing/PriceCalculator.cs", FileStatus.Modified),
                ("tools/Stamp.cs", FileStatus.NotInSolution),
            },
            result.Files.Select(file => (file.Path, file.Status)));

        var orderService = Assert.Single(result.Files, file => file.Path == OrderServiceFile);
        Assert.Equal((4, 1), (orderService.Symbols, orderService.UnmappedLines)); // the unmapped line is the new using directive
    }

    [Theory]
    [InlineData("02-removals.diff")]
    [InlineData("02-removals-u0.diff")]
    public async Task Removed_code_is_attributed_to_the_enclosing_declaration(string diffName)
    {
        var result = await FindAsync(diffName);

        // A removed method marks its class, a removed statement marks its method,
        // a removed attribute marks the member below it, a removed blank line is ignored.
        Assert.Equal(
            new[]
            {
                ("T:SampleShop.Orders.OrderService", SymbolChange.Modified, 2),
                ("M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)", SymbolChange.Modified, 1),
                ("M:SampleShop.Orders.OrderService.CalculateDiscountRate(SampleShop.Orders.Order)", SymbolChange.Modified, 1),
            },
            result.Symbols.Select(symbol => (symbol.Id, symbol.Change, symbol.ChangedLines)));
    }

    private Task<ChangedSymbolsResult> FindAsync(string diffName) =>
        ChangedSymbolFinder.FindAsync(fixture.Snapshot, TestPaths.ReadDiff(diffName), TestContext.Current.CancellationToken);
}
