using RoslynReview.Core.Workspace;

namespace RoslynReview.Tests.Infrastructure;

/// <summary>Loads tests/fixtures/SampleShop once for every test in the <see cref="SampleShopCollection"/>.</summary>
public sealed class SampleShopFixture : IAsyncLifetime
{
    private SolutionHost? _host;

    public SolutionSnapshot Snapshot { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        await DotNetCli.RestoreAsync(TestPaths.SampleShopSolution);
        _host = new SolutionHost(new WorkspaceOptions(TestPaths.SampleShopSolution, RepositoryRoot: TestPaths.SampleShop));
        Snapshot = await _host.GetSnapshotAsync();
    }

    public ValueTask DisposeAsync()
    {
        _host?.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Tests that load SampleShop run one at a time: concurrent design-time builds of the same
/// projects would race on files in obj/.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SampleShopCollection : ICollectionFixture<SampleShopFixture>
{
    public const string Name = "SampleShop";
}
