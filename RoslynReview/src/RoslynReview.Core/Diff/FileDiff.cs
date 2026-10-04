namespace RoslynReview.Core.Diff;

public enum FileChangeKind
{
    Modified,
    Added,
    Deleted,
    Renamed,
}

/// <summary>A line added by the diff, numbered in the new version of the file (1-based).</summary>
public readonly record struct DiffLine(int Number, string Text);

/// <summary>
/// Lines removed without a replacement, located right after line <see cref="AfterLine"/>
/// of the new file (0 means the start of the file).
/// </summary>
public sealed record DiffDeletion(int AfterLine, IReadOnlyList<string> Lines);

/// <summary>Changes to a single file in a unified diff.</summary>
public sealed record FileDiff(
    string? OldPath,
    string? NewPath,
    FileChangeKind Kind,
    bool IsBinary,
    IReadOnlyList<DiffLine> AddedLines,
    IReadOnlyList<DiffDeletion> Deletions)
{
    /// <summary>The path in the new version, or the old path for deleted files.</summary>
    public string Path => NewPath ?? OldPath ?? string.Empty;
}
