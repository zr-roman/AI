using System.Globalization;
using System.Text.Json;

namespace SqlAnalyst.Core.Guard;

public sealed record PlanSummary(double TotalCost, double PlanRows, IReadOnlyList<string> Relations, IReadOnlyList<string> Functions);

/// <summary>
/// Второй рубеж: смотрит на план запроса (EXPLAIN (FORMAT JSON, VERBOSE)), который строит сам PostgreSQL.
/// Здесь уже нет догадок лексера: видно, какие отношения и функции реально будут затронуты и сколько это стоит.
/// Отношения схемы витрины раскрываются в таблицы-источники, поэтому проверяем не «только analytics»,
/// а «ни одного системного каталога» — права на остальное отсекает сама БД.
/// </summary>
public static class PlanInspector
{
    private static readonly string[] SystemSchemas = ["pg_catalog", "information_schema", "pg_toast"];

    public static PlanSummary Parse(string explainJson)
    {
        using var document = JsonDocument.Parse(explainJson);
        var root = document.RootElement[0].GetProperty("Plan");

        var relations = new SortedSet<string>(StringComparer.Ordinal);
        var functions = new SortedSet<string>(StringComparer.Ordinal);
        Walk(root, relations, functions);

        return new PlanSummary(
            root.GetProperty("Total Cost").GetDouble(),
            root.GetProperty("Plan Rows").GetDouble(),
            [.. relations],
            [.. functions]);
    }

    /// <returns>Причина отказа или null, если план допустим.</returns>
    public static string? Check(PlanSummary plan, double maxCost)
    {
        var system = plan.Relations.FirstOrDefault(r => SystemSchemas.Any(s => r.StartsWith(s + ".", StringComparison.Ordinal)));
        if (system is not null)
        {
            return $"Запрос обращается к системному каталогу ({system}). Это запрещено.";
        }

        var function = plan.Functions.FirstOrDefault(f => f.StartsWith("pg_", StringComparison.Ordinal) || f.Contains("_to_xml", StringComparison.Ordinal));
        if (function is not null)
        {
            return $"Запрос вызывает запрещённую функцию {function}.";
        }

        if (plan.TotalCost > maxCost)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Запрос слишком тяжёлый: оценка стоимости {plan.TotalCost:F0} при лимите {maxCost:F0}. Добавь фильтры или агрегацию, убери лишние соединения.");
        }

        return null;
    }

    private static void Walk(JsonElement node, ISet<string> relations, ISet<string> functions)
    {
        if (node.TryGetProperty("Relation Name", out var relation))
        {
            var schema = node.TryGetProperty("Schema", out var s) ? s.GetString() : "?";
            relations.Add($"{schema}.{relation.GetString()}");
        }

        if (node.TryGetProperty("Function Name", out var function))
        {
            functions.Add(function.GetString()!);
        }

        if (node.TryGetProperty("Plans", out var children))
        {
            foreach (var child in children.EnumerateArray())
            {
                Walk(child, relations, functions);
            }
        }
    }
}
