using RoslynReview.Core.Diff;
using RoslynReview.Tests.Infrastructure;

namespace RoslynReview.Tests;

/// <summary>Guards the fixture diffs against edits to the SampleShop files they describe.</summary>
public sealed class FixtureDiffTests
{
    [Theory]
    [InlineData("01-feature.diff")]
    [InlineData("02-removals.diff")]
    [InlineData("02-removals-u0.diff")]
    public void New_side_of_each_diff_matches_the_fixture_files(string diffName)
    {
        foreach (var file in UnifiedDiffParser.Parse(TestPaths.ReadDiff(diffName)))
        {
            if (file.Kind == FileChangeKind.Deleted)
                continue;

            var lines = File.ReadAllLines(Path.Combine(TestPaths.SampleShop, file.Path));
            foreach (var added in file.AddedLines)
            {
                Assert.True(added.Number <= lines.Length, $"{diffName}: {file.Path} has no line {added.Number}");
                Assert.Equal(added.Text, lines[added.Number - 1]);
            }

            foreach (var deletion in file.Deletions)
                Assert.InRange(deletion.AfterLine, 0, lines.Length);
        }
    }
}
