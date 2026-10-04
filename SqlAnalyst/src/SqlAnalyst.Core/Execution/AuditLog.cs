using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SqlAnalyst.Core.Execution;

public sealed record AuditRecord(
    DateTimeOffset At,
    string Tool,
    string? Sql,
    string? Purpose,
    string Verdict,
    string? Reason,
    int? Rows,
    long? DurationMs,
    double? PlanCost,
    IReadOnlyList<string>? Flags);

/// <summary>Журнал каждого обращения к БД: что спросили, что решили рубежи защиты, сколько вернули.</summary>
public sealed class AuditLog(AnalystOptions options, ILogger<AuditLog> logger)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly Lock _lock = new();

    public void Write(AuditRecord record)
    {
        var line = JsonSerializer.Serialize(record, Json);

        if (record.Verdict is "blocked" or "security")
        {
            logger.LogWarning("audit {Record}", line);
        }
        else
        {
            logger.LogInformation("audit {Record}", line);
        }

        if (string.IsNullOrWhiteSpace(options.AuditLogPath))
        {
            return;
        }

        lock (_lock)
        {
            var path = Path.GetFullPath(options.AuditLogPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }
}
