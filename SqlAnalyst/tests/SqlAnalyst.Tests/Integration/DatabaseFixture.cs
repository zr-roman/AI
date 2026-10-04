using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SqlAnalyst.Core;
using SqlAnalyst.Core.Execution;

namespace SqlAnalyst.Tests.Integration;

/// <summary>
/// Интеграционные тесты идут против настоящего PostgreSQL с данными из db/init.
/// Строки подключения — из переменных окружения; без них тесты пропускаются (локально без БД всё равно зелено).
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    public const string AnalystVariable = "SQLANALYST_TEST_CONNECTION";
    public const string AdminVariable = "SQLANALYST_TEST_ADMIN_CONNECTION";

    public static string? AnalystConnection => Environment.GetEnvironmentVariable(AnalystVariable);

    public static string? AdminConnection => Environment.GetEnvironmentVariable(AdminVariable);

    public ServiceProvider? Services { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (AnalystConnection is null)
        {
            return;
        }

        Services = Build(AnalystConnection, new Dictionary<string, string?>
        {
            ["Analyst:StatementTimeoutSeconds"] = "1",
            ["Analyst:QueriesPerMinute"] = "1000",
            ["Analyst:CanaryTokens:0"] = "CANARY-7f3a9c2e",
        });

        await Services.InitializeAnalystAsync(TestContext.Current.CancellationToken);
    }

    public static ServiceProvider Build(string connectionString, IDictionary<string, string?>? settings = null)
    {
        var values = new Dictionary<string, string?> { ["Analyst:ConnectionString"] = connectionString };
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var services = new ServiceCollection().AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSqlAnalyst(configuration);
        return services.BuildServiceProvider();
    }

    public QueryExecutor Executor
    {
        get
        {
            Assert.SkipWhen(Services is null, $"Не задана переменная {AnalystVariable}: интеграционные тесты пропущены.");
            return Services!.GetRequiredService<QueryExecutor>();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Services is not null)
        {
            await Services.DisposeAsync();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "database";
}
