using RoslynReview.Core.Diff;
using RoslynReview.Tests.Infrastructure;

namespace RoslynReview.Tests.Diff;

public sealed class UnifiedDiffParserTests
{
    [Fact]
    public void Added_lines_are_numbered_in_the_new_file()
    {
        var diff = Lines(
            "diff --git a/src/A.cs b/src/A.cs",
            "index 1111111..2222222 100644",
            "--- a/src/A.cs",
            "+++ b/src/A.cs",
            "@@ -10,4 +10,5 @@ class A",
            " line10",
            "-old11",
            "+new11",
            "+new12",
            " line13",
            " line14");

        var file = Assert.Single(UnifiedDiffParser.Parse(diff));

        Assert.Equal("src/A.cs", file.Path);
        Assert.Equal(FileChangeKind.Modified, file.Kind);
        Assert.Equal(new[] { new DiffLine(11, "new11"), new DiffLine(12, "new12") }, file.AddedLines);
        Assert.Empty(file.Deletions); // a replaced line is described by the lines that replace it
    }

    [Fact]
    public void Removed_lines_without_replacement_are_located_after_the_previous_new_line()
    {
        var diff = Lines(
            "--- a/A.cs",
            "+++ b/A.cs",
            "@@ -1,5 +1,3 @@",
            " one",
            "-two",
            "-three",
            " four",
            " five");

        var file = Assert.Single(UnifiedDiffParser.Parse(diff));

        var deletion = Assert.Single(file.Deletions);
        Assert.Equal(1, deletion.AfterLine);
        Assert.Equal(new[] { "two", "three" }, deletion.Lines);
        Assert.Empty(file.AddedLines);
    }

    [Fact]
    public void Zero_context_hunks_are_located_correctly()
    {
        // git diff -U0: an empty range such as "+4,0" names the line before the change.
        var diff = Lines(
            "--- a/A.cs",
            "+++ b/A.cs",
            "@@ -5 +4,0 @@",
            "-five",
            "@@ -8,0 +9,2 @@",
            "+inserted9",
            "+inserted10");

        var file = Assert.Single(UnifiedDiffParser.Parse(diff));

        Assert.Equal(4, Assert.Single(file.Deletions).AfterLine);
        Assert.Equal(new[] { 9, 10 }, file.AddedLines.Select(line => line.Number));
    }

    [Fact]
    public void New_deleted_renamed_and_binary_files_are_recognized()
    {
        var diff = Lines(
            "diff --git a/src/New.cs b/src/New.cs",
            "new file mode 100644",
            "index 0000000..1111111",
            "--- /dev/null",
            "+++ b/src/New.cs",
            "@@ -0,0 +1,2 @@",
            "+class New",
            "+{}",
            "diff --git a/src/Old.cs b/src/Old.cs",
            "deleted file mode 100644",
            "index 1111111..0000000",
            "--- a/src/Old.cs",
            "+++ /dev/null",
            "@@ -1 +0,0 @@",
            "-class Old {}",
            "diff --git a/src/Before.cs b/src/After.cs",
            "similarity index 90%",
            "rename from src/Before.cs",
            "rename to src/After.cs",
            "index 1111111..2222222 100644",
            "--- a/src/Before.cs",
            "+++ b/src/After.cs",
            "@@ -1 +1 @@",
            "-class Before {}",
            "+class After {}",
            "diff --git a/logo.png b/logo.png",
            "index 1111111..2222222 100644",
            "Binary files a/logo.png and b/logo.png differ");

        var files = UnifiedDiffParser.Parse(diff);

        Assert.Equal(
            new[] { FileChangeKind.Added, FileChangeKind.Deleted, FileChangeKind.Renamed, FileChangeKind.Modified },
            files.Select(file => file.Kind));
        Assert.Equal(new[] { "src/New.cs", "src/Old.cs", "src/After.cs", "logo.png" }, files.Select(file => file.Path));
        Assert.Null(files[0].OldPath);
        Assert.Null(files[1].NewPath);
        Assert.Equal("src/Before.cs", files[2].OldPath);
        Assert.True(files[3].IsBinary);
        Assert.Equal(new[] { 1, 2 }, files[0].AddedLines.Select(line => line.Number));
    }

    [Fact]
    public void Windows_line_endings_and_no_newline_markers_are_tolerated()
    {
        var diff =
            "--- a/A.cs\r\n+++ b/A.cs\r\n@@ -1,2 +1,2 @@\r\n first\r\n-second\r\n" +
            "\\ No newline at end of file\r\n+changed\r\n\\ No newline at end of file\r\n";

        var file = Assert.Single(UnifiedDiffParser.Parse(diff));

        Assert.Equal(new DiffLine(2, "changed"), Assert.Single(file.AddedLines));
        Assert.Empty(file.Deletions);
    }

    [Fact]
    public void Quoted_paths_with_escaped_utf8_are_decoded()
    {
        var diff = Lines(
            "diff --git \"a/src/Caf\\303\\251.cs\" \"b/src/Caf\\303\\251.cs\"",
            "--- \"a/src/Caf\\303\\251.cs\"",
            "+++ \"b/src/Caf\\303\\251.cs\"",
            "@@ -1 +1 @@",
            "-old",
            "+new");

        Assert.Equal("src/Café.cs", Assert.Single(UnifiedDiffParser.Parse(diff)).Path);
    }

    [Fact]
    public void Plain_unified_diffs_without_git_headers_are_supported()
    {
        var diff = Lines(
            "--- src/A.cs\t2026-10-01 10:00:00.000000000 +0300",
            "+++ src/A.cs\t2026-10-02 10:00:00.000000000 +0300",
            "@@ -1 +1,2 @@",
            " keep",
            "+added",
            "--- src/B.cs\t2026-10-01 10:00:00.000000000 +0300",
            "+++ src/B.cs\t2026-10-02 10:00:00.000000000 +0300",
            "@@ -3 +3 @@",
            "-x",
            "+y");

        var files = UnifiedDiffParser.Parse(diff);

        Assert.Equal(new[] { "src/A.cs", "src/B.cs" }, files.Select(file => file.Path));
        Assert.Equal(new DiffLine(2, "added"), Assert.Single(files[0].AddedLines));
    }

    [Fact]
    public void Parses_every_file_of_the_fixture_diff()
    {
        var files = UnifiedDiffParser.Parse(TestPaths.ReadDiff("01-feature.diff"));

        Assert.Equal(
            new[]
            {
                "README.md",
                "src/SampleShop.Api/Program.cs",
                "src/SampleShop.Core/Legacy/OldPricing.cs",
                "src/SampleShop.Core/Orders/OrderService.cs",
                "src/SampleShop.Core/Pricing/Coupon.cs",
                "src/SampleShop.Core/Pricing/PriceCalculator.cs",
                "tools/Stamp.cs",
            },
            files.Select(file => file.Path));
        Assert.Equal(FileChangeKind.Deleted, files[2].Kind);
        Assert.Equal(FileChangeKind.Added, files[4].Kind);
    }

    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";
}
