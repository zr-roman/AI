using System.Runtime.CompilerServices;
using Crm.GraphQL.Tests.Infrastructure;

namespace Crm.GraphQL.Tests;

/// <summary>
/// Snapshot-тест контракта: текущая схема (SDL) сравнивается с эталоном Snapshots/schema.graphql из репозитория.
/// Любое изменение схемы — новое поле, переименование, смена nullability — роняет тест, и его видно в ревью диффа эталона.
/// Так ломающее изменение не уедет к клиентам незаметно.
/// </summary>
public sealed class SchemaTests(CrmApiFactory api) : IClassFixture<CrmApiFactory>
{
    [Fact]
    public async Task Schema_matches_snapshot()
    {
        using var client = api.CreateClient(CrmApiFactory.AnnaKey);
        var actual = Normalize(await client.GetStringAsync("/graphql/schema.graphql", TestContext.Current.CancellationToken));

        var snapshot = Path.Combine(SnapshotsDirectory(), "schema.graphql");
        var received = Path.Combine(SnapshotsDirectory(), "schema.received.graphql");

        if (!File.Exists(snapshot))
        {
            Directory.CreateDirectory(SnapshotsDirectory());
            await File.WriteAllTextAsync(snapshot, actual, TestContext.Current.CancellationToken);
            Assert.Fail($"Эталона схемы не было, он создан: {snapshot}. Проверь его и добавь в репозиторий.");
        }

        var expected = Normalize(await File.ReadAllTextAsync(snapshot, TestContext.Current.CancellationToken));
        if (expected != actual)
        {
            await File.WriteAllTextAsync(received, actual, TestContext.Current.CancellationToken);
            Assert.Fail(
                $"Схема GraphQL изменилась. Сравни {snapshot} и {received}. " +
                "Если изменение ожидаемое и не ломает клиентов, замени эталон полученным файлом.");
        }

        File.Delete(received);
    }

    // Эталон лежит рядом с исходниками теста, а не в bin: так его изменения попадают в git
    private static string SnapshotsDirectory([CallerFilePath] string sourceFile = "") =>
        Path.Combine(Path.GetDirectoryName(sourceFile)!, "Snapshots");

    private static string Normalize(string sdl) => sdl.ReplaceLineEndings("\n").Trim() + "\n";
}
