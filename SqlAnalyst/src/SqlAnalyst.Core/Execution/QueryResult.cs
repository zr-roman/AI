namespace SqlAnalyst.Core.Execution;

public sealed record QueryColumn(string Name, string Type);

public sealed record QueryResult(
    IReadOnlyList<QueryColumn> Columns,
    IReadOnlyList<string?[]> Rows,
    bool Truncated,
    long DurationMs,
    double? PlanCost);

/// <summary>Ответ инструмента: текст для модели и признак ошибки (уходит как isError в MCP).</summary>
public sealed record ToolOutcome(string Text, bool IsError)
{
    public static ToolOutcome Ok(string text) => new(text, false);

    public static ToolOutcome Error(string text) => new(text, true);
}
