using System.ComponentModel;
using System.Text;
using SqlAnalyst.Core.Charts;

namespace SqlAnalyst.Agent;

public sealed record ChartSeriesInput(
    [property: Description("Название ряда для легенды, например «Выручка»")] string Name,
    [property: Description("Значения ряда, по одному на каждую подпись labels, в том же порядке")] double[] Values);

/// <summary>
/// Локальный инструмент агента: рисует график в HTML-файл. В БД не ходит, сеть не трогает,
/// пишет только в свою папку и только файл с именем, которое собирает сам.
/// </summary>
public sealed class ChartTool(string directory)
{
    public const string Name = "render_chart";

    [Description("Строит график по уже полученным данным и сохраняет его в HTML-файл. Возвращает путь к файлу. Данные бери только из результатов run_query.")]
    public string RenderChart(
        [Description("Заголовок графика, например «Отток клиентов по месяцам, 2026»")] string title,
        [Description("Тип: bar — сравнение категорий или периодов, line — динамика во времени")] string kind,
        [Description("Подписи по оси X (категории или периоды), не больше 60")] string[] labels,
        [Description("Ряды данных, от 1 до 4")] ChartSeriesInput[] series,
        [Description("Подпись оси значений, например «₽» или «клиентов»")] string? valueAxisLabel = null)
    {
        var chartKind = kind.Trim().ToLowerInvariant() switch
        {
            "bar" or "column" => ChartKind.Bar,
            "line" => ChartKind.Line,
            _ => throw new ChartSpecException("kind должен быть bar или line."),
        };

        var spec = new ChartSpec(title, chartKind, labels, [.. series.Select(s => new ChartSeries(s.Name, s.Values))], valueAxisLabel);
        var html = ChartRenderer.RenderHtml(spec);

        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Slug(title)}.html");
        File.WriteAllText(path, html, Encoding.UTF8);

        return $"График сохранён: {Path.GetFullPath(path)}";
    }

    /// <summary>Имя файла только из букв, цифр и дефисов: заголовок не может увести запись в другую папку.</summary>
    internal static string Slug(string title)
    {
        var sb = new StringBuilder();

        foreach (var c in title.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }

            if (sb.Length >= 40)
            {
                break;
            }
        }

        var slug = sb.ToString().Trim('-');
        return slug.Length == 0 ? "chart" : slug;
    }
}
