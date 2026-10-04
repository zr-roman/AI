using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynReview.Core.Diff;
using RoslynReview.Core.Workspace;

namespace RoslynReview.Core.Navigation;

[JsonConverter(typeof(JsonStringEnumConverter<SymbolChange>))]
public enum SymbolChange
{
    /// <summary>Every line of the declaration is new.</summary>
    [JsonStringEnumMemberName("added")] Added,

    [JsonStringEnumMemberName("modified")] Modified,
}

[JsonConverter(typeof(JsonStringEnumConverter<FileStatus>))]
public enum FileStatus
{
    [JsonStringEnumMemberName("modified")] Modified,
    [JsonStringEnumMemberName("added")] Added,
    [JsonStringEnumMemberName("deleted")] Deleted,
    [JsonStringEnumMemberName("renamed")] Renamed,
    [JsonStringEnumMemberName("binary")] Binary,
    [JsonStringEnumMemberName("not-csharp")] NotCSharp,
    [JsonStringEnumMemberName("not-in-solution")] NotInSolution,
}

/// <summary>A member or type touched by the diff, located in the new version of the file.</summary>
public sealed record ChangedSymbol(
    string Id,
    string Name,
    string Kind,
    SymbolChange Change,
    string File,
    int StartLine,
    int EndLine,
    int ChangedLines,
    string? Accessibility);

/// <param name="Symbols">Number of changed symbols reported for the file.</param>
/// <param name="UnmappedLines">Changed lines outside any member, e.g. using directives.</param>
public sealed record ChangedFile(
    string Path,
    FileStatus Status,
    string? OldPath = null,
    int? Symbols = null,
    int? UnmappedLines = null);

public sealed record ChangedSymbolsResult(IReadOnlyList<ChangedSymbol> Symbols, IReadOnlyList<ChangedFile> Files);

/// <summary>Maps the hunks of a unified diff to the C# declarations they touch.</summary>
/// <remarks>
/// Added lines map to the innermost member (method, property, field, ...) or type that contains them.
/// Removed lines map to the declaration that encloses the gap they left, so deleting a whole member
/// marks its containing type as modified, while deleting an attribute or comment marks the member below it.
/// Whitespace-only changes are ignored. Members of a type that is new as a whole are not listed separately.
/// </remarks>
public static class ChangedSymbolFinder
{
    public static async Task<ChangedSymbolsResult> FindAsync(
        SolutionSnapshot snapshot, string unifiedDiff, CancellationToken cancellationToken = default)
    {
        var symbols = new List<ChangedSymbol>();
        var files = new List<ChangedFile>();

        foreach (var fileDiff in UnifiedDiffParser.Parse(unifiedDiff))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var oldPath = fileDiff.Kind == FileChangeKind.Renamed ? fileDiff.OldPath : null;

            if (!fileDiff.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(new ChangedFile(fileDiff.Path, FileStatus.NotCSharp, oldPath));
                continue;
            }

            if (fileDiff.Kind == FileChangeKind.Deleted || fileDiff.IsBinary)
            {
                files.Add(new ChangedFile(fileDiff.Path, fileDiff.IsBinary ? FileStatus.Binary : FileStatus.Deleted, oldPath));
                continue;
            }

            var documentIds = snapshot.FindDocuments(fileDiff.Path);
            if (documentIds.IsEmpty || snapshot.Solution.GetDocument(documentIds[0]) is not { } document)
            {
                files.Add(new ChangedFile(fileDiff.Path, FileStatus.NotInSolution, oldPath));
                continue;
            }

            var (fileSymbols, unmapped) = await MapAsync(document, fileDiff, cancellationToken).ConfigureAwait(false);
            symbols.AddRange(fileSymbols);
            files.Add(new ChangedFile(fileDiff.Path, ToStatus(fileDiff.Kind), oldPath, fileSymbols.Count, unmapped > 0 ? unmapped : null));
        }

        return new ChangedSymbolsResult(symbols, files);
    }

    private static async Task<(List<ChangedSymbol> Symbols, int UnmappedLines)> MapAsync(
        Document document, FileDiff diff, CancellationToken cancellationToken)
    {
        var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
        if (root is null || model is null)
        {
            return ([], 0);
        }

        var changedLinesByDeclaration = new Dictionary<SyntaxNode, int>();
        var unmapped = 0;

        foreach (var added in diff.AddedLines)
        {
            if (string.IsNullOrWhiteSpace(added.Text))
            {
                continue;
            }

            if (added.Number < 1 || added.Number > text.Lines.Count)
            {
                unmapped++; // the diff does not match the file on disk
                continue;
            }

            var position = FirstNonWhitespace(text, text.Lines[added.Number - 1]);
            Record(Declarations.Find(root, position), 1);
        }

        foreach (var deletion in diff.Deletions)
        {
            var removed = deletion.Lines.Count(line => !string.IsNullOrWhiteSpace(line));
            if (removed == 0)
            {
                continue;
            }

            var gap = LineStart(text, deletion.AfterLine + 1);
            var declaration = deletion.Lines.All(IsAttributeOrComment)
                ? Declarations.Find(root, SkipWhitespace(text, gap)) // removed [Attribute] or comment: belongs to the member below
                : Declarations.Find(root, gap, strict: true); // removed code: belongs to whatever encloses the gap
            Record(declaration, removed);
        }

        var addedLines = diff.AddedLines.Select(line => line.Number).ToHashSet();
        var addedTypes = new HashSet<SyntaxNode>();
        var found = new List<(SyntaxNode Declaration, ChangedSymbol Symbol)>();

        foreach (var (declaration, changedLines) in changedLinesByDeclaration.OrderBy(pair => pair.Key.SpanStart))
        {
            var lines = text.Lines.GetLinePositionSpan(Declarations.Span(declaration));
            var startLine = lines.Start.Line + 1;
            var endLine = lines.End.Line + 1;
            var change = IsEntirelyAdded(text, startLine, endLine, addedLines) ? SymbolChange.Added : SymbolChange.Modified;
            if (change == SymbolChange.Added && declaration is BaseTypeDeclarationSyntax)
            {
                addedTypes.Add(declaration);
            }

            foreach (var symbol in Declarations.DeclaredSymbols(model, declaration, cancellationToken))
            {
                found.Add((declaration, new ChangedSymbol(
                    SymbolText.Id(symbol),
                    SymbolText.Name(symbol),
                    SymbolText.Kind(symbol),
                    change,
                    diff.Path,
                    startLine,
                    endLine,
                    changedLines,
                    SymbolText.Accessibility(symbol))));
            }
        }

        var symbols = found
            .Where(item => !item.Declaration.Ancestors().Any(addedTypes.Contains))
            .Select(item => item.Symbol)
            .ToList();
        return (symbols, unmapped);

        void Record(SyntaxNode? declaration, int lineCount)
        {
            if (declaration is null)
            {
                unmapped += lineCount;
            }
            else
            {
                changedLinesByDeclaration[declaration] = changedLinesByDeclaration.GetValueOrDefault(declaration) + lineCount;
            }
        }
    }

    private static bool IsEntirelyAdded(SourceText text, int startLine, int endLine, HashSet<int> addedLines)
    {
        for (var line = startLine; line <= endLine; line++)
        {
            if (!addedLines.Contains(line) && !string.IsNullOrWhiteSpace(text.Lines[line - 1].ToString()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAttributeOrComment(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.Length == 0 || trimmed[0] is '[' or '/' or '*' or '#';
    }

    private static int FirstNonWhitespace(SourceText text, TextLine line)
    {
        for (var i = line.Start; i < line.End; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }

        return line.Start;
    }

    private static int SkipWhitespace(SourceText text, int position)
    {
        while (position < text.Length && char.IsWhiteSpace(text[position]))
        {
            position++;
        }

        return position;
    }

    private static int LineStart(SourceText text, int lineNumber) =>
        lineNumber < 1 ? 0 : lineNumber > text.Lines.Count ? text.Length : text.Lines[lineNumber - 1].Start;

    private static FileStatus ToStatus(FileChangeKind kind) => kind switch
    {
        FileChangeKind.Added => FileStatus.Added,
        FileChangeKind.Renamed => FileStatus.Renamed,
        _ => FileStatus.Modified,
    };
}
