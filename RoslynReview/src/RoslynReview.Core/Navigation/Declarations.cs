using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace RoslynReview.Core.Navigation;

/// <summary>
/// Finds the declaration a source position belongs to, at the granularity a reviewer thinks in:
/// members and types. Lambdas, local functions and accessors belong to their member; top-level
/// statements belong to the compilation unit, which stands for the synthesized entry point.
/// </summary>
internal static class Declarations
{
    /// <summary>
    /// Returns the innermost member or type declaration at <paramref name="position"/>. In strict mode the
    /// position must lie strictly inside the declaration's span (attributes included, leading trivia excluded),
    /// so the gap between two members resolves to their containing type.
    /// </summary>
    public static SyntaxNode? Find(SyntaxNode root, int position, bool strict = false)
    {
        position = Math.Clamp(position, 0, Math.Max(0, root.FullSpan.End - 1));
        var token = root.FindToken(position);
        if (token.Parent is { } parent)
        {
            foreach (var node in parent.AncestorsAndSelf())
            {
                if (node is GlobalStatementSyntax)
                    break;

                if (IsDeclaration(node) && (!strict || StrictlyContains(node.Span, position)))
                    return node;
            }
        }

        if (root is CompilationUnitSyntax unit && TopLevelStatementsSpan(unit) is { } statements &&
            (strict ? StrictlyContains(statements, position) : statements.Contains(position)))
        {
            return unit;
        }

        return null;
    }

    /// <summary>The symbols a declaration found by <see cref="Find"/> declares (a field declaration can declare several).</summary>
    public static IEnumerable<ISymbol> DeclaredSymbols(SemanticModel model, SyntaxNode declaration, CancellationToken cancellationToken) =>
        declaration switch
        {
            BaseFieldDeclarationSyntax field => field.Declaration.Variables
                .Select(variable => model.GetDeclaredSymbol(variable, cancellationToken))
                .OfType<ISymbol>(),
            CompilationUnitSyntax unit => model.GetDeclaredSymbol(unit, cancellationToken) is { } entryPoint ? [entryPoint] : [],
            _ => model.GetDeclaredSymbol(declaration, cancellationToken) is { } symbol ? [symbol] : [],
        };

    /// <summary>The symbol declared at <paramref name="position"/>: for a multi-variable field, the variable containing it.</summary>
    public static ISymbol? DeclaredSymbolAt(SemanticModel model, SyntaxNode declaration, int position, CancellationToken cancellationToken)
    {
        if (declaration is BaseFieldDeclarationSyntax field &&
            field.Declaration.Variables.FirstOrDefault(variable => variable.FullSpan.Contains(position)) is { } variable)
        {
            return model.GetDeclaredSymbol(variable, cancellationToken);
        }

        return DeclaredSymbols(model, declaration, cancellationToken).FirstOrDefault();
    }

    /// <summary>The span to report for a declaration: top-level statements span from the first to the last statement.</summary>
    public static TextSpan Span(SyntaxNode declaration) =>
        declaration is CompilationUnitSyntax unit && TopLevelStatementsSpan(unit) is { } statements ? statements : declaration.Span;

    private static bool IsDeclaration(SyntaxNode node) =>
        node is BaseMethodDeclarationSyntax
            or BasePropertyDeclarationSyntax
            or BaseFieldDeclarationSyntax
            or EnumMemberDeclarationSyntax
            or BaseTypeDeclarationSyntax
            or DelegateDeclarationSyntax;

    private static TextSpan? TopLevelStatementsSpan(CompilationUnitSyntax unit)
    {
        var statements = unit.Members.OfType<GlobalStatementSyntax>().ToList();
        return statements.Count == 0 ? null : TextSpan.FromBounds(statements[0].SpanStart, statements[^1].Span.End);
    }

    private static bool StrictlyContains(TextSpan span, int position) => span.Start < position && position < span.End;
}
