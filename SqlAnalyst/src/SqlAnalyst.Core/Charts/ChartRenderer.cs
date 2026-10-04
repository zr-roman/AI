using System.Globalization;
using System.Net;
using System.Text;

namespace SqlAnalyst.Core.Charts;

public enum ChartKind
{
    Bar,
    Line,
}

public sealed record ChartSeries(string Name, IReadOnlyList<double> Values);

public sealed record ChartSpec(string Title, ChartKind Kind, IReadOnlyList<string> Labels, IReadOnlyList<ChartSeries> Series, string? ValueAxisLabel = null);

public sealed class ChartSpecException(string message) : Exception(message);

/// <summary>
/// Рисует график в самодостаточный HTML с inline SVG: без скриптов, CDN и внешних ресурсов.
/// Подписи и названия приходят от модели, а модель могла прочитать их из недоверенных данных,
/// поэтому весь текст HTML-экранируется — никакой разметки или скрипта через данные в файл не попадёт.
/// </summary>
public static class ChartRenderer
{
    public const int MaxSeries = 4;
    public const int MaxLabels = 60;

    // Категориальная палитра (первые четыре слота проверенного порядка): светлая и тёмная темы
    private static readonly string[] LightSeries = ["#2a78d6", "#eb6834", "#1baf7a", "#eda100"];
    private static readonly string[] DarkSeries = ["#3987e5", "#d95926", "#199e70", "#c98500"];

    private const double Width = 760, Height = 420, Left = 72, Right = 24, Top = 24, Bottom = 48;

    public static void Validate(ChartSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Title))
        {
            throw new ChartSpecException("Нужен заголовок графика.");
        }

        if (spec.Labels.Count is 0 or > MaxLabels)
        {
            throw new ChartSpecException($"Подписей по оси X должно быть от 1 до {MaxLabels}. Агрегируй данные.");
        }

        if (spec.Series.Count is 0 or > MaxSeries)
        {
            throw new ChartSpecException($"Рядов должно быть от 1 до {MaxSeries}. Остальные объедини в «Прочее» или построй отдельный график.");
        }

        foreach (var series in spec.Series)
        {
            if (series.Values.Count != spec.Labels.Count)
            {
                throw new ChartSpecException($"В ряду «{series.Name}» {series.Values.Count} значений, а подписей {spec.Labels.Count}.");
            }

            if (series.Values.Any(v => !double.IsFinite(v)))
            {
                throw new ChartSpecException($"В ряду «{series.Name}» есть нечисловые значения.");
            }
        }
    }

    public static string RenderHtml(ChartSpec spec)
    {
        Validate(spec);

        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="ru">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta http-equiv="Content-Security-Policy" content="default-src 'none'; style-src 'unsafe-inline'">
            <title>{{E(spec.Title)}}</title>
            <style>
            :root { color-scheme: light; --surface: #fcfcfb; --text: #0b0b0b; --muted: #52514e; --grid: #e4e3df;
              {{string.Join(" ", LightSeries.Select((c, i) => $"--s{i + 1}: {c};"))}} }
            @media (prefers-color-scheme: dark) { :root { color-scheme: dark; --surface: #1a1a19; --text: #ffffff; --muted: #c3c2b7; --grid: #34342f;
              {{string.Join(" ", DarkSeries.Select((c, i) => $"--s{i + 1}: {c};"))}} } }
            body { margin: 0; padding: 24px 16px; background: var(--surface); color: var(--text); font: 14px/1.45 system-ui, sans-serif; }
            main { max-width: 800px; margin: 0 auto; }
            h1 { font-size: 18px; margin: 0 0 4px; }
            .legend { display: flex; flex-wrap: wrap; gap: 16px; margin: 8px 0; color: var(--muted); }
            .legend span::before { content: ""; display: inline-block; width: 10px; height: 10px; border-radius: 2px; margin-right: 6px; background: var(--c); }
            svg { width: 100%; height: auto; display: block; }
            svg text { fill: var(--muted); font-size: 12px; }
            svg .value { fill: var(--text); font-size: 12px; }
            .mark:hover { opacity: .8; }
            details { margin-top: 12px; color: var(--muted); }
            table { border-collapse: collapse; margin-top: 8px; font-variant-numeric: tabular-nums; }
            th, td { padding: 4px 10px; border-bottom: 1px solid var(--grid); text-align: right; }
            th:first-child, td:first-child { text-align: left; }
            </style>
            </head>
            <body>
            <main>
            <h1>{{E(spec.Title)}}</h1>

            """);

        if (spec.Series.Count > 1)
        {
            html.Append("<div class=\"legend\">");
            for (var s = 0; s < spec.Series.Count; s++)
            {
                html.Append($"<span style=\"--c: var(--s{s + 1})\">{E(spec.Series[s].Name)}</span>");
            }

            html.Append("</div>\n");
        }

        html.Append(RenderSvg(spec));
        html.Append("<details><summary>Таблица данных</summary><table><tr><th></th>");

        foreach (var series in spec.Series)
        {
            html.Append($"<th>{E(series.Name)}</th>");
        }

        html.Append("</tr>");

        for (var i = 0; i < spec.Labels.Count; i++)
        {
            html.Append($"<tr><td>{E(spec.Labels[i])}</td>");
            foreach (var series in spec.Series)
            {
                html.Append($"<td>{Number(series.Values[i])}</td>");
            }

            html.Append("</tr>");
        }

        html.Append("</table></details>\n</main>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static string RenderSvg(ChartSpec spec)
    {
        var values = spec.Series.SelectMany(s => s.Values).ToList();
        var (min, max, ticks) = NiceScale(Math.Min(0, values.Min()), Math.Max(0, values.Max()));

        var plotWidth = Width - Left - Right;
        var plotHeight = Height - Top - Bottom;
        double Y(double v) => Top + (max - v) / (max - min) * plotHeight;

        var svg = new StringBuilder();
        svg.Append(Inv($"<svg viewBox=\"0 0 {Width} {Height}\" role=\"img\" aria-label=\"{E(spec.Title)}\">\n"));

        // Сетка и подписи оси значений: тонкие, сплошные, на шаг темнее фона
        foreach (var tick in ticks)
        {
            var y = Y(tick);
            svg.Append(Inv($"<line x1=\"{Left}\" x2=\"{Width - Right}\" y1=\"{y:F1}\" y2=\"{y:F1}\" stroke=\"var(--grid)\" stroke-width=\"1\"/>"));
            svg.Append(Inv($"<text x=\"{Left - 8}\" y=\"{y + 4:F1}\" text-anchor=\"end\">{Compact(tick)}</text>\n"));
        }

        if (spec.ValueAxisLabel is { Length: > 0 } axis)
        {
            svg.Append(Inv($"<text x=\"14\" y=\"{Top + plotHeight / 2:F1}\" transform=\"rotate(-90 14 {Top + plotHeight / 2:F1})\" text-anchor=\"middle\">{E(axis)}</text>\n"));
        }

        var band = plotWidth / spec.Labels.Count;
        var labelStep = (int)Math.Ceiling(spec.Labels.Count / (plotWidth / 70));

        for (var i = 0; i < spec.Labels.Count; i += labelStep)
        {
            var x = Left + band * (i + 0.5);
            svg.Append(Inv($"<text x=\"{x:F1}\" y=\"{Height - Bottom + 20}\" text-anchor=\"middle\">{E(Truncate(spec.Labels[i], 14))}</text>\n"));
        }

        if (spec.Kind == ChartKind.Bar)
        {
            AppendBars(svg, spec, band, Y);
        }
        else
        {
            AppendLines(svg, spec, band, Y);
        }

        svg.Append("</svg>\n");
        return svg.ToString();
    }

    private static void AppendBars(StringBuilder svg, ChartSpec spec, double band, Func<double, double> y)
    {
        const double gap = 2;
        var seriesCount = spec.Series.Count;
        var barWidth = Math.Min(24, (band * 0.7 - gap * (seriesCount - 1)) / seriesCount);
        var groupWidth = barWidth * seriesCount + gap * (seriesCount - 1);
        var showValues = seriesCount == 1 && spec.Labels.Count <= 12;
        var zero = y(0);

        for (var i = 0; i < spec.Labels.Count; i++)
        {
            var groupStart = Left + band * i + (band - groupWidth) / 2;

            for (var s = 0; s < seriesCount; s++)
            {
                var value = spec.Series[s].Values[i];
                var x = groupStart + s * (barWidth + gap);
                var end = y(value);

                svg.Append(Inv($"<path class=\"mark\" fill=\"var(--s{s + 1})\" d=\"{BarPath(x, barWidth, zero, end)}\">"));
                svg.Append($"<title>{E(spec.Labels[i])} · {E(spec.Series[s].Name)}: {Number(value)}</title></path>\n");

                if (showValues)
                {
                    var labelY = value >= 0 ? end - 6 : end + 14;
                    svg.Append(Inv($"<text class=\"value\" x=\"{x + barWidth / 2:F1}\" y=\"{labelY:F1}\" text-anchor=\"middle\">{Compact(value)}</text>\n"));
                }
            }
        }

        svg.Append(Inv($"<line x1=\"{Left}\" x2=\"{Width - Right}\" y1=\"{zero:F1}\" y2=\"{zero:F1}\" stroke=\"var(--muted)\" stroke-width=\"1\"/>\n"));
    }

    private static void AppendLines(StringBuilder svg, ChartSpec spec, double band, Func<double, double> y)
    {
        for (var s = 0; s < spec.Series.Count; s++)
        {
            var series = spec.Series[s];
            var points = series.Values.Select((v, i) => (X: Left + band * (i + 0.5), Y: y(v))).ToList();

            svg.Append(Inv($"<polyline fill=\"none\" stroke=\"var(--s{s + 1})\" stroke-width=\"2\" stroke-linejoin=\"round\" stroke-linecap=\"round\" points=\"{string.Join(' ', points.Select(p => Inv($"{p.X:F1},{p.Y:F1}")))}\"/>\n"));

            for (var i = 0; i < points.Count; i++)
            {
                svg.Append(Inv($"<circle class=\"mark\" cx=\"{points[i].X:F1}\" cy=\"{points[i].Y:F1}\" r=\"4\" fill=\"var(--s{s + 1})\" stroke=\"var(--surface)\" stroke-width=\"2\">"));
                svg.Append($"<title>{E(spec.Labels[i])} · {E(series.Name)}: {Number(series.Values[i])}</title></circle>\n");
            }

            // Подпись значения только у последней точки
            var last = points[^1];
            svg.Append(Inv($"<text class=\"value\" x=\"{last.X:F1}\" y=\"{last.Y - 10:F1}\" text-anchor=\"middle\">{Compact(series.Values[^1])}</text>\n"));
        }
    }

    /// <summary>Столбик от нулевой линии со скруглением 4px только на конце значения.</summary>
    private static string BarPath(double x, double width, double zero, double end)
    {
        var height = Math.Abs(zero - end);
        var r = Math.Min(4, Math.Min(width / 2, height));
        var sign = end <= zero ? 1 : -1;

        return Inv($"M{x:F1},{zero:F1} V{end + sign * r:F1} Q{x:F1},{end:F1} {x + r:F1},{end:F1} H{x + width - r:F1} Q{x + width:F1},{end:F1} {x + width:F1},{end + sign * r:F1} V{zero:F1} Z");
    }

    internal static (double Min, double Max, IReadOnlyList<double> Ticks) NiceScale(double min, double max)
    {
        if (min == max)
        {
            max = min + 1;
        }

        var rawStep = (max - min) / 5;
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(rawStep)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(m => m * magnitude).First(s => s >= rawStep);
        var niceMin = Math.Floor(min / step) * step;
        var niceMax = Math.Ceiling(max / step) * step;

        var ticks = new List<double>();
        for (var t = niceMin; t <= niceMax + step / 2; t += step)
        {
            ticks.Add(Math.Round(t, 10));
        }

        return (niceMin, niceMax, ticks);
    }

    private static string Compact(double value)
    {
        var abs = Math.Abs(value);
        return abs switch
        {
            >= 1_000_000_000 => Inv($"{value / 1_000_000_000:0.#} млрд"),
            >= 1_000_000 => Inv($"{value / 1_000_000:0.#} млн"),
            >= 10_000 => Inv($"{value / 1_000:0.#} тыс"),
            _ => Inv($"{value:0.##}"),
        };
    }

    private static string Number(double value) => value.ToString("#,0.##", CultureInfo.InvariantCulture).Replace(",", " ", StringComparison.Ordinal);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string E(string text) => WebUtility.HtmlEncode(text);

    private static string Inv(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
