using SqlAnalyst.Agent;
using SqlAnalyst.Core.Charts;

namespace SqlAnalyst.Tests.Charts;

public sealed class ChartRendererTests
{
    private static ChartSpec Spec(ChartKind kind = ChartKind.Bar, string label = "Q1") =>
        new("Отток по кварталам", kind, [label, "Q2", "Q3"], [new ChartSeries("Клиенты", [12, 7, 15])], "клиентов");

    [Theory]
    [InlineData(ChartKind.Bar)]
    [InlineData(ChartKind.Line)]
    public void Renders_self_contained_html(ChartKind kind)
    {
        var html = ChartRenderer.RenderHtml(Spec(kind));

        Assert.StartsWith("<!doctype html>", html);
        Assert.Contains("<svg", html);
        Assert.Contains("Content-Security-Policy", html);
        Assert.DoesNotContain("<script", html);
        Assert.DoesNotContain("http://", html);
        Assert.Contains("<title>Q2 · Клиенты: 7</title>", html);
        Assert.Contains("Таблица данных", html);
    }

    [Fact]
    public void Text_from_data_is_html_encoded()
    {
        var html = ChartRenderer.RenderHtml(Spec(label: "<script>alert(1)</script>"));

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Legend_appears_only_for_several_series()
    {
        var single = ChartRenderer.RenderHtml(Spec());
        var multi = ChartRenderer.RenderHtml(Spec() with { Series = [new("A", [1, 2, 3]), new("B", [3, 2, 1])] });

        Assert.DoesNotContain("class=\"legend\"", single);
        Assert.Contains("class=\"legend\"", multi);
    }

    [Fact]
    public void Rejects_inconsistent_or_oversized_data()
    {
        Assert.Throws<ChartSpecException>(() => ChartRenderer.Validate(Spec() with { Series = [new("A", [1, 2])] }));
        Assert.Throws<ChartSpecException>(() => ChartRenderer.Validate(Spec() with { Series = [.. Enumerable.Range(0, 5).Select(i => new ChartSeries($"S{i}", [1, 2, 3]))] }));
        Assert.Throws<ChartSpecException>(() => ChartRenderer.Validate(Spec() with { Series = [new("A", [1, double.NaN, 3])] }));
    }

    [Fact]
    public void Nice_scale_starts_at_zero_and_covers_the_maximum()
    {
        var (min, max, ticks) = ChartRenderer.NiceScale(0, 163_417.6);

        Assert.Equal(0, min);
        Assert.True(max >= 163_417.6);
        Assert.Equal(max, ticks[^1]);
    }

    [Theory]
    [InlineData("../../etc/passwd", "etc-passwd")]
    [InlineData("Отток: Q3 2026", "отток-q3-2026")]
    [InlineData("///", "chart")]
    public void Chart_file_names_cannot_escape_the_charts_folder(string title, string slug)
    {
        Assert.Equal(slug, ChartTool.Slug(title));
    }
}
