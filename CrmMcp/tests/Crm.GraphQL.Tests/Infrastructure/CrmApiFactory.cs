using System.Net.Http.Headers;
using Crm.Core.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Crm.GraphQL.Tests.Infrastructure;

/// <summary>
/// Поднимает Crm.Api целиком в памяти (TestServer): аутентификация по API-ключу, /graphql, сервисы Crm.Core.
/// База — настоящий PostgreSQL: сервисы используют ILIKE и jsonb, in-memory провайдер EF их не выполнит.
/// Каждый тестовый класс получает свою временную БД с демо-данными, поэтому классы идут параллельно и не мешают друг другу.
/// Сервер PostgreSQL задаёт переменная окружения CRM_TESTS_POSTGRES, по умолчанию — тот же, что в appsettings.json Crm.Api.
/// </summary>
public sealed class CrmApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Анна Смирнова, Admin.</summary>
    public const string AnnaKey = "anna-dev-key";

    /// <summary>Борис Козлов, Manager.</summary>
    public const string BorisKey = "boris-dev-key";

    private const string DefaultServer = "Host=localhost;Port=5433;Username=postgres;Password=12345";

    private readonly string _connectionString = new NpgsqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("CRM_TESTS_POSTGRES") ?? DefaultServer)
    {
        Database = $"crm_tests_{Guid.NewGuid():N}",
    }.ConnectionString;

    /// <summary>Счётчик SQL-запросов к БД этого хоста — для проверки, что DataLoader убирает N+1.</summary>
    public SqlCommandCounter SqlCommands { get; } = new();

    public HttpClient CreateClient(string apiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Как при dotnet run: в Development схема GraphQL открыта для интроспекции
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:Crm", _connectionString);
        builder.UseSetting("Crm:SeedDemoData", "true");

        builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<CrmDbContext>(
                (_, options) => options.AddInterceptors(SqlCommands),
                ServiceLifetime.Singleton));
    }

    public ValueTask InitializeAsync()
    {
        // Первое обращение к Server запускает хост, а Program.cs создаёт схему БД и заполняет её демо-данными
        _ = Server;
        return ValueTask.CompletedTask;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        // Соединения пула держат БД открытой; WITH (FORCE) закроет и те, что остались
        NpgsqlConnection.ClearAllPools();
        var builder = new NpgsqlConnectionStringBuilder(_connectionString);
        var database = builder.Database;
        builder.Database = "postgres";

        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
        await command.ExecuteNonQueryAsync();
    }
}
