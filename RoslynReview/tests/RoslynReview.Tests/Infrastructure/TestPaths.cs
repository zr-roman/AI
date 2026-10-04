namespace RoslynReview.Tests.Infrastructure;

internal static class TestPaths
{
    public static string Fixtures { get; } = FindFixtures();

    public static string SampleShop => Path.Combine(Fixtures, "SampleShop");

    public static string SampleShopSolution => Path.Combine(SampleShop, "SampleShop.slnx");

    public static string ReadDiff(string name) => File.ReadAllText(Path.Combine(Fixtures, "diffs", name));

    private static string FindFixtures()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "tests", "fixtures");

            if (File.Exists(Path.Combine(candidate, "SampleShop", "SampleShop.slnx")))
                return candidate;
        }

        throw new InvalidOperationException($"tests/fixtures not found above {AppContext.BaseDirectory}");
    }
}
