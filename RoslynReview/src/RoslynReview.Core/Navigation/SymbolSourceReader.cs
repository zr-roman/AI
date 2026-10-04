using System.Globalization;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynReview.Core.Workspace;

namespace RoslynReview.Core.Navigation;

/// <param name="SymbolId">Set on outline lines: the id of the member declared on that line.</param>
public readonly record struct SourceLine(int Number, string Text, string? SymbolId = null);

/// <param name="IsOutline">The declaration was too long, so only its header and one line per member are included.</param>
/// <param name="OmittedLines">Lines cut off at the end because of the line limit.</param>
public sealed record SourceExcerpt(string File, int StartLine, int EndLine, IReadOnlyList<SourceLine> Lines, bool IsOutline, int OmittedLines);

public sealed record SymbolSource(string Id, string Name, string Kind, int MaxLines, IReadOnlyList<SourceExcerpt> Excerpts)
{
    /// <summary>Line-numbered text for the agent, one block per declaration (partial types have several).</summary>
    public string ToDisplayString()
    {
        var builder = new StringBuilder();
        foreach (var excerpt in Excerpts)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(CultureInfo.InvariantCulture, $"// {Kind} {Name}").AppendLine();
            builder.Append(CultureInfo.InvariantCulture, $"// {excerpt.File}:{excerpt.StartLine}-{excerpt.EndLine}");
            if (excerpt.IsOutline)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $" (outline: {excerpt.EndLine - excerpt.StartLine + 1} lines exceed maxLines={MaxLines}; request a member by its id for full source)");
            }

            builder.AppendLine();
            var width = excerpt.EndLine.ToString(CultureInfo.InvariantCulture).Length;
            foreach (var line in excerpt.Lines)
            {
                builder.Append(line.Number.ToString(CultureInfo.InvariantCulture).PadLeft(width)).Append(" | ").Append(line.Text);
                if (line.SymbolId is not null)
                {
                    builder.Append("  // ").Append(line.SymbolId);
                }

                builder.AppendLine();
            }

            if (excerpt.OmittedLines > 0)
            {
                builder.Append(CultureInfo.InvariantCulture,
                    $"// {excerpt.OmittedLines} more lines not shown; call again with a larger maxLines").AppendLine();
            }
        }

        return builder.ToString();
    }
}

/// <summary>Reads the source of a symbol, including its XML doc comment.</summary>
public static class SymbolSourceReader
{
    public const int DefaultMaxLines = 150;

    public static async Task<SymbolSource> ReadAsync(
        SolutionSnapshot snapshot, string symbolId, int maxLines = DefaultMaxLines, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);
        var symbol = await SymbolResolver.ResolveAsync(snapshot.Solution, symbolId, cancellationToken).ConfigureAwait(false);

        var excerpts = new List<SourceExcerpt>();
        foreach (var reference in symbol.DeclaringSyntaxReferences)
        {
            var declaration = DisplayedNode(await reference.GetSyntaxAsync(cancellationToken).ConfigureAwait(false));
            if (snapshot.Solution.GetDocument(declaration.SyntaxTree) is not { } document)
            {
                continue;
            }

            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var first = text.Lines.GetLineFromPosition(StartIncludingDocComment(declaration)).LineNumber;
            var last = text.Lines.GetLineFromPosition(declaration.Span.End).LineNumber;
            var file = snapshot.RelativePath(document);

            if (last - first + 1 > maxLines && declaration is TypeDeclarationSyntax type &&
                await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false) is { } model)
            {
                excerpts.Add(new SourceExcerpt(file, first + 1, last + 1, Outline(text, type, first, model, cancellationToken), IsOutline: true, OmittedLines: 0));
                continue;
            }

            var shownLast = Math.Min(last, first + maxLines - 1);
            var lines = Enumerable.Range(first, shownLast - first + 1)
                .Select(index => new SourceLine(index + 1, text.Lines[index].ToString().TrimEnd()))
                .ToList();
            excerpts.Add(new SourceExcerpt(file, first + 1, last + 1, lines, IsOutline: false, OmittedLines: last - shownLast));
        }

        return new SymbolSource(SymbolText.Id(symbol), SymbolText.Name(symbol), SymbolText.Kind(symbol), maxLines, excerpts);
    }

    // The type header, one line per member with that member's id, and the closing brace.
    private static List<SourceLine> Outline(SourceText text, TypeDeclarationSyntax type, int firstLine, SemanticModel model, CancellationToken cancellationToken)
    {
        var lines = new List<SourceLine>();
        var headerEnd = type.OpenBraceToken.IsKind(SyntaxKind.None) || type.OpenBraceToken.IsMissing
            ? text.Lines.GetLineFromPosition(type.Span.End).LineNumber
            : text.Lines.GetLineFromPosition(type.OpenBraceToken.SpanStart).LineNumber;
        for (var index = firstLine; index <= headerEnd; index++)
        {
            lines.Add(new SourceLine(index + 1, text.Lines[index].ToString().TrimEnd()));
        }

        foreach (var member in type.Members)
        {
            var symbol = member is BaseFieldDeclarationSyntax field
                ? model.GetDeclaredSymbol(field.Declaration.Variables[0], cancellationToken)
                : model.GetDeclaredSymbol(member, cancellationToken);
            var line = text.Lines.GetLineFromPosition(SignatureStart(member));
            lines.Add(new SourceLine(line.LineNumber + 1, line.ToString().TrimEnd(), symbol is null ? null : SymbolText.Id(symbol)));
        }

        if (!type.CloseBraceToken.IsMissing && !type.CloseBraceToken.IsKind(SyntaxKind.None))
        {
            var close = text.Lines.GetLineFromPosition(type.CloseBraceToken.SpanStart);
            lines.Add(new SourceLine(close.LineNumber + 1, close.ToString().TrimEnd()));
        }

        return lines;
    }

    // Fields are declared by a variable declarator and record properties by a primary constructor
    // parameter; show the whole field declaration or record instead of that fragment.
    private static SyntaxNode DisplayedNode(SyntaxNode node) => node switch
    {
        VariableDeclaratorSyntax { Parent.Parent: BaseFieldDeclarationSyntax field } => field,
        ParameterSyntax { Parent.Parent: TypeDeclarationSyntax type } => type,
        _ => node,
    };

    private static int StartIncludingDocComment(SyntaxNode node)
    {
        foreach (var trivia in node.GetLeadingTrivia())
        {
            if (trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
                trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                return trivia.SpanStart;
            }
        }

        return node.SpanStart;
    }

    // The first token after any attribute lists: the line that reads like the member's signature.
    private static int SignatureStart(MemberDeclarationSyntax member)
    {
        foreach (var child in member.ChildNodesAndTokens())
        {
            if (!child.IsKind(SyntaxKind.AttributeList))
            {
                return child.SpanStart;
            }
        }

        return member.SpanStart;
    }
}
