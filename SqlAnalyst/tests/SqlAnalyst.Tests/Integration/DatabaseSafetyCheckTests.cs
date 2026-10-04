using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SqlAnalyst.Core.Execution;

namespace SqlAnalyst.Tests.Integration;

public sealed class DatabaseSafetyCheckTests
{
    [Fact]
    public async Task Accepts_the_read_only_analyst_role()
    {
        Assert.SkipWhen(DatabaseFixture.AnalystConnection is null, "Нет БД.");
        await using var services = DatabaseFixture.Build(DatabaseFixture.AnalystConnection!);

        await DatabaseSafetyCheck.VerifyAsync(services.GetRequiredService<NpgsqlDataSource>(), "analytics", TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Refuses_to_start_under_a_superuser()
    {
        Assert.SkipWhen(DatabaseFixture.AdminConnection is null, $"Не задана {DatabaseFixture.AdminVariable}.");
        await using var services = DatabaseFixture.Build(DatabaseFixture.AdminConnection!);

        var ex = await Assert.ThrowsAsync<UnsafeConnectionException>(() =>
            DatabaseSafetyCheck.VerifyAsync(services.GetRequiredService<NpgsqlDataSource>(), "analytics", TestContext.Current.CancellationToken));

        Assert.Contains(ex.Problems, p => p.Contains("SUPERUSER", StringComparison.Ordinal));
    }
}
