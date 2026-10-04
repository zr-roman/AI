using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SqlAnalyst.Core.Execution;
using SqlAnalyst.Core.Guard;
using SqlAnalyst.Core.Output;

namespace SqlAnalyst.Core;

public static class AnalystServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует конвейер аналитика. Перед стартом хоста обязательно вызвать InitializeAnalystAsync:
    /// он проверяет роль БД и загружает список схем для SqlGuard.
    /// </summary>
    public static IServiceCollection AddSqlAnalyst(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AnalystOptions.Section).Get<AnalystOptions>() ?? new AnalystOptions();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException("Не задана строка подключения Analyst:ConnectionString (или переменная Analyst__ConnectionString).");
        }

        services.AddSingleton(options);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(_ => new NpgsqlDataSourceBuilder(options.ConnectionString)
        {
            Name = "sql-analyst",
        }.Build());
        services.AddSingleton<SchemaCatalog>();
        services.AddSingleton<AuditLog>();
        services.AddSingleton(sp => new QueryRateLimiter(sp.GetRequiredService<TimeProvider>(), options.QueriesPerMinute));
        services.AddSingleton(_ => new ResultFormatter(options));
        services.AddSingleton<GuardPolicyHolder>();
        services.AddSingleton(sp => new SqlGuard(sp.GetRequiredService<GuardPolicyHolder>().Policy));
        services.AddSingleton<QueryExecutor>();

        return services;
    }

    public static async Task InitializeAnalystAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var options = services.GetRequiredService<AnalystOptions>();
        var dataSource = services.GetRequiredService<NpgsqlDataSource>();

        await DatabaseSafetyCheck.VerifyAsync(dataSource, options.AllowedSchema, cancellationToken);

        var schemas = await services.GetRequiredService<SchemaCatalog>().ListSchemasAsync(cancellationToken);
        services.GetRequiredService<GuardPolicyHolder>().Policy = new GuardPolicy
        {
            AllowedSchema = options.AllowedSchema,
            KnownSchemas = schemas,
        };
    }
}

/// <summary>Политика зависит от списка схем в БД, который известен только после подключения.</summary>
internal sealed class GuardPolicyHolder
{
    public GuardPolicy Policy { get; set; } = new();
}
