using Microsoft.CodeAnalysis;

namespace RoslynReview.Core.Navigation;

/// <summary>The symbol id does not resolve to source in the loaded solution. The message is meant for the agent.</summary>
public sealed class SymbolNotFoundException(string symbolId, string message) : Exception(message)
{
    public string SymbolId { get; } = symbolId;
}

/// <summary>Resolves documentation comment ids (the ids returned by the tools) to symbols declared in source.</summary>
public static class SymbolResolver
{
    public static async Task<ISymbol> ResolveAsync(Solution solution, string symbolId, CancellationToken cancellationToken = default)
    {
        if (!LooksLikeId(symbolId))
        {
            throw new SymbolNotFoundException(symbolId,
                $"'{symbolId}' is not a symbol id. Pass an id exactly as returned by another tool, e.g. " +
                "'M:MyApp.Orders.OrderService.PlaceOrderAsync(MyApp.Orders.Order,System.Threading.CancellationToken)'.");
        }

        foreach (var project in solution.Projects)
        {
            if (project.Language != LanguageNames.CSharp ||
                await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not { } compilation)
            {
                continue;
            }

            // Every project sees the types of the projects it references, so keep only the declaring project's copy.
            foreach (var candidate in DocumentationCommentId.GetSymbolsForDeclarationId(symbolId, compilation))
            {
                if (candidate.Locations.Any(location => location.SourceTree is { } tree && compilation.ContainsSyntaxTree(tree)))
                {
                    return candidate;
                }
            }

            // Top-level statements compile into "<Main>$", whose id ("M:Program.{Main}$(System.String[])")
            // cannot be parsed back. With several such projects, the first one wins.
            if (compilation.GetEntryPoint(cancellationToken) is { } entryPoint &&
                SymbolText.IsTopLevelStatements(entryPoint) && SymbolText.Id(entryPoint) == symbolId)
            {
                return entryPoint;
            }
        }

        throw new SymbolNotFoundException(symbolId,
            $"No symbol with id '{symbolId}' is declared in the solution. Ids are case-sensitive and include " +
            "parameter types; symbols from NuGet packages or the framework have no source here.");
    }

    // T: type, M: method, P: property, F: field, E: event. Namespaces (N:) have no single declaration.
    private static bool LooksLikeId(string id) =>
        id.Length > 2 && id[1] == ':' && id[0] is 'T' or 'M' or 'P' or 'F' or 'E';
}
