namespace Crm.Core.Domain;

/// <summary>Запись аудита: кто, через какой канал и с какими аргументами вызвал операцию.</summary>
public sealed class AuditEntry
{
    public long Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public int? UserId { get; set; }

    /// <summary>Канал: mcp-http, mcp-stdio.</summary>
    public required string Channel { get; set; }

    /// <summary>Имя операции, например имя MCP-инструмента.</summary>
    public required string Operation { get; set; }

    /// <summary>Аргументы вызова в JSON (jsonb).</summary>
    public string? Arguments { get; set; }

    public bool Succeeded { get; set; }
    public string? Error { get; set; }
    public int DurationMs { get; set; }
}
