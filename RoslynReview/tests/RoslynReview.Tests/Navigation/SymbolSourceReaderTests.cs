using RoslynReview.Core.Navigation;
using RoslynReview.Tests.Infrastructure;

namespace RoslynReview.Tests.Navigation;

[Collection(SampleShopCollection.Name)]
public sealed class SymbolSourceReaderTests(SampleShopFixture fixture)
{
    private const string PlaceOrderId =
        "M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)";

    [Fact]
    public async Task Returns_the_member_with_its_doc_comment()
    {
        var source = await ReadAsync(PlaceOrderId);

        var excerpt = Assert.Single(source.Excerpts);
        Assert.Equal(("src/SampleShop.Core/Orders/OrderService.cs", 13, 33), (excerpt.File, excerpt.StartLine, excerpt.EndLine));
        Assert.Equal(21, excerpt.Lines.Count);
        Assert.StartsWith("    /// <summary>Places the order if", excerpt.Lines[0].Text);
        Assert.False(excerpt.IsOutline);
    }

    [Fact]
    public async Task Display_text_has_a_header_and_line_numbers()
    {
        var text = (await ReadAsync(PlaceOrderId)).ToDisplayString();

        Assert.StartsWith("// method OrderService.PlaceOrderAsync(Order, CancellationToken)", text);
        Assert.Contains("// src/SampleShop.Core/Orders/OrderService.cs:13-33", text);
        Assert.Contains("14 |     public async Task<OrderResult> PlaceOrderAsync(Order order, CancellationToken cancellationToken)", text);
    }

    [Fact]
    public async Task Long_types_come_back_as_an_outline_with_member_ids()
    {
        var source = await ReadAsync("T:SampleShop.Orders.OrderService", maxLines: 20);

        var excerpt = Assert.Single(source.Excerpts);
        Assert.True(excerpt.IsOutline);
        Assert.Contains(excerpt.Lines, line => line.Number == 8 && line.SymbolId == "F:SampleShop.Orders.OrderService._inventory");
        Assert.Contains(excerpt.Lines, line => line.Number == 14 && line.SymbolId == PlaceOrderId);
        Assert.Contains(excerpt.Lines, line => line.Number == 47 && line.Text == "}");
        Assert.Contains("outline", source.ToDisplayString());
    }

    [Fact]
    public async Task Long_members_are_cut_with_a_note()
    {
        var source = await ReadAsync(PlaceOrderId, maxLines: 5);

        var excerpt = Assert.Single(source.Excerpts);
        Assert.Equal(5, excerpt.Lines.Count);
        Assert.Equal(16, excerpt.OmittedLines);
        Assert.Contains("16 more lines not shown", source.ToDisplayString());
    }

    [Fact]
    public async Task Record_properties_show_the_record_and_fields_their_declaration()
    {
        var property = await ReadAsync("P:SampleShop.Orders.Order.Lines");
        Assert.Contains("public sealed record Order(Guid Id, string CustomerId, IReadOnlyList<OrderLine> Lines)", property.ToDisplayString());

        var constant = await ReadAsync("F:SampleShop.Pricing.PriceCalculator.LoyaltyDiscountRate");
        var line = Assert.Single(Assert.Single(constant.Excerpts).Lines);
        Assert.Equal(new SourceLine(5, "    public const decimal LoyaltyDiscountRate = 0.05m;"), line);
    }

    [Fact]
    public async Task Top_level_statements_resolve_from_the_id_other_tools_report()
    {
        var source = await ReadAsync("M:Program.{Main}$(System.String[])");

        Assert.Equal("src/SampleShop.Api/Program.cs", Assert.Single(source.Excerpts).File);
        Assert.Contains("Console.WriteLine(endpoints.PreviewDiscount(lines));", source.ToDisplayString());
    }

    [Theory]
    [InlineData("M:SampleShop.Orders.OrderService.DoesNotExist")]
    [InlineData("OrderService.PlaceOrderAsync")]
    [InlineData("N:SampleShop.Orders")]
    public async Task Unknown_or_malformed_ids_are_reported(string symbolId)
    {
        var error = await Assert.ThrowsAsync<SymbolNotFoundException>(() => ReadAsync(symbolId));

        Assert.Equal(symbolId, error.SymbolId);
    }

    private Task<SymbolSource> ReadAsync(string symbolId, int maxLines = SymbolSourceReader.DefaultMaxLines) =>
        SymbolSourceReader.ReadAsync(fixture.Snapshot, symbolId, maxLines, TestContext.Current.CancellationToken);
}
