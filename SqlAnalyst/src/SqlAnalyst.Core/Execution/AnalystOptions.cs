namespace SqlAnalyst.Core.Execution;

/// <summary>Настройки сервера (секция "Analyst" в appsettings.json или переменные Analyst__*).</summary>
public sealed class AnalystOptions
{
    public const string Section = "Analyst";

    /// <summary>Подключение под read-only ролью. Под суперпользователем сервер откажется стартовать.</summary>
    public string ConnectionString { get; set; } = "";

    public string AllowedSchema { get; set; } = "analytics";

    public int DefaultMaxRows { get; set; } = 100;

    public int HardMaxRows { get; set; } = 500;

    public int StatementTimeoutSeconds { get; set; } = 5;

    /// <summary>Верхняя граница оценки стоимости плана (EXPLAIN): тяжёлые запросы отклоняются до выполнения.</summary>
    public double MaxPlanCost { get; set; } = 200_000;

    public int MaxCellChars { get; set; } = 300;

    public int MaxResultChars { get; set; } = 30_000;

    public int QueriesPerMinute { get; set; } = 30;

    /// <summary>
    /// Метки, которые лежат только в закрытых таблицах. Если метка встретилась в результате — изоляция нарушена,
    /// результат блокируется целиком, а в аудит пишется инцидент.
    /// </summary>
    public List<string> CanaryTokens { get; set; } = [];

    public InjectionMode InjectionMode { get; set; } = InjectionMode.Redact;

    /// <summary>JSONL-журнал всех запросов. Пусто — только в лог (stderr).</summary>
    public string? AuditLogPath { get; set; }
}

public enum InjectionMode
{
    /// <summary>Подозрительная ячейка заменяется пометкой — модель не видит текст атаки.</summary>
    Redact,

    /// <summary>Ячейка остаётся, но помечается предупреждением.</summary>
    Flag,
}
