using Microsoft.Extensions.Configuration;
using RoslynReview.McpServer;
using RoslynReview.Tests.Infrastructure;

namespace RoslynReview.Tests.Server;

public sealed class ServerConfigurationTests
{
    [Fact]
    public void Relative_paths_resolve_against_the_working_directory()
    {
        var options = ServerConfiguration.Resolve(
            Configuration(("solution", "SampleShop/SampleShop.slnx"), ("repo-root", "SampleShop")),
            TestPaths.Fixtures);

        Assert.Equal(TestPaths.SampleShopSolution, options.SolutionPath);
        Assert.Equal(TestPaths.SampleShop, options.RepositoryRoot);
    }

    [Fact]
    public void Without_arguments_the_only_solution_in_the_working_directory_is_used()
    {
        var options = ServerConfiguration.Resolve(Configuration(), TestPaths.SampleShop);

        Assert.Equal(TestPaths.SampleShopSolution, options.SolutionPath);
        Assert.Null(options.RepositoryRoot);
    }

    [Fact]
    public void A_missing_solution_is_a_configuration_error()
    {
        var error = Assert.Throws<ServerConfigurationException>(() =>
            ServerConfiguration.Resolve(Configuration(("solution", "Missing.slnx")), TestPaths.Fixtures));

        Assert.Contains("Missing.slnx", error.Message);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
