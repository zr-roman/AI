using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using RoslynReview.Core.Workspace;

namespace RoslynReview.Core.Navigation;

/// <param name="Code">The source line of the reference, trimmed.</param>
/// <param name="Via">Set when the reference targets a related member, e.g. the interface method this symbol implements.</param>
/// <param name="Implicit">Set for compiler-generated calls such as GetEnumerator in foreach or Dispose in using.</param>
public sealed record CallSite(string File, int Line, string Code, string? Via = null, bool? Implicit = null);

public sealed record Caller(string Id, string Name, string Kind, string Project, IReadOnlyList<CallSite> Sites);

/// <param name="TotalSites">All references found; only the first maxResults are listed when <see cref="Truncated"/> is set.</param>
public sealed record CallersResult(string Id, string Name, int TotalSites, bool Truncated, IReadOnlyList<Caller> Callers);

/// <summary>Finds who calls or references a symbol, grouped by the calling member.</summary>
public static class CallerFinder
{
    public const int DefaultMaxResults = 50;
    private const int MaxCodeLength = 160;

    public static async Task<CallersResult> FindAsync(
        SolutionSnapshot snapshot, string symbolId, int maxResults = DefaultMaxResults, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxResults, 1);
        var solution = snapshot.Solution;
        var symbol = await SymbolResolver.ResolveAsync(solution, symbolId, cancellationToken).ConfigureAwait(false);

        // FindReferences cascades to related symbols: interface members this symbol implements,
        // members it overrides or that override it, accessors, constructors of a type.
        // Each usage is attributed to the member or type declaration it appears in, signature included.
        var references = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);

        // A file compiled into several projects (multi-targeting) yields one location per project.
        var seen = new HashSet<(string, int)>();
        var found = new List<(ISymbol Caller, string Project, CallSite Site)>();
        foreach (var referenced in references)
        {
            var via = IsSameSymbol(referenced.Definition, symbol) ? null : SymbolText.Name(referenced.Definition);

            foreach (var location in referenced.Locations)
            {
                var document = location.Document;
                var position = location.Location.SourceSpan.Start;
                if (location.IsCandidateLocation || !seen.Add((document.FilePath ?? document.Name, position)))
                {
                    continue;
                }

                var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
                var model = await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false);
                if (root is null || model is null || IsInDocumentationComment(root, position) ||
                    Declarations.Find(root, position) is not { } declaration ||
                    Declarations.DeclaredSymbolAt(model, declaration, position, cancellationToken) is not { } caller)
                {
                    continue; // e.g. a using directive, which belongs to no member
                }

                var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
                var line = text.Lines.GetLineFromPosition(position);
                var site = new CallSite(
                    snapshot.RelativePath(document),
                    line.LineNumber + 1,
                    Shorten(line.ToString().Trim()),
                    via,
                    location.IsImplicit ? true : null);
                found.Add((caller, document.Project.Name, site));
            }
        }

        var ordered = found
            .OrderBy(item => item.Site.File, StringComparer.Ordinal)
            .ThenBy(item => item.Site.Line)
            .ToList();
        var callers = ordered
            .Take(maxResults)
            .GroupBy(item => item.Caller, SymbolEqualityComparer.Default)
            .Select(group => new Caller(
                SymbolText.Id(group.Key),
                SymbolText.Name(group.Key),
                SymbolText.Kind(group.Key),
                group.First().Project,
                [.. group.Select(item => item.Site)]))
            .ToList();

        return new CallersResult(SymbolText.Id(symbol), SymbolText.Name(symbol), ordered.Count, ordered.Count > maxResults, callers);
    }

    // A type's constructors are part of the type, so `new T()` is a direct usage rather than one "via" the constructor.
    private static bool IsSameSymbol(ISymbol definition, ISymbol symbol) =>
        SymbolEqualityComparer.Default.Equals(definition.OriginalDefinition, symbol.OriginalDefinition) ||
        (symbol is INamedTypeSymbol && definition is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor &&
            SymbolEqualityComparer.Default.Equals(constructor.ContainingType.OriginalDefinition, symbol.OriginalDefinition));

    // <see cref="..."/> mentions are not usages.
    private static bool IsInDocumentationComment(SyntaxNode root, int position) =>
        root.FindToken(position, findInsideTrivia: true).Parent?
            .AncestorsAndSelf()
            .OfType<DocumentationCommentTriviaSyntax>()
            .Any() == true;

    private static string Shorten(string code) =>
        code.Length <= MaxCodeLength ? code : string.Concat(code.AsSpan(0, MaxCodeLength - 1), "…");
}
