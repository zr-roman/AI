using Microsoft.CodeAnalysis;

namespace RoslynReview.Core.Navigation;

/// <summary>How symbols are named in tool output: short display names for reading, documentation ids for follow-up calls.</summary>
internal static class SymbolText
{
    private static readonly SymbolDisplayFormat DisplayFormat = SymbolDisplayFormat.CSharpShortErrorMessageFormat;

    /// <summary>Stable documentation comment id, e.g. <c>M:Shop.OrderService.Place(Shop.Order)</c>.</summary>
    public static string Id(ISymbol symbol) => symbol.GetDocumentationCommentId() ?? symbol.ToDisplayString();

    public static string Name(ISymbol symbol) =>
        IsTopLevelStatements(symbol) ? "<top-level statements>" : symbol.ToDisplayString(DisplayFormat);

    public static string Kind(ISymbol symbol) => symbol switch
    {
        INamedTypeSymbol { IsRecord: true, TypeKind: TypeKind.Struct } => "record struct",
        INamedTypeSymbol { IsRecord: true } => "record",
        INamedTypeSymbol type => type.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => "type",
        },
        IMethodSymbol method => method.MethodKind switch
        {
            MethodKind.Constructor or MethodKind.StaticConstructor => "constructor",
            MethodKind.Destructor => "finalizer",
            MethodKind.UserDefinedOperator or MethodKind.Conversion => "operator",
            MethodKind.PropertyGet or MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove => "accessor",
            MethodKind.LocalFunction => "local function",
            _ => "method",
        },
        IPropertySymbol { IsIndexer: true } => "indexer",
        IPropertySymbol => "property",
        IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } => "enum member",
        IFieldSymbol { IsConst: true } => "constant",
        IFieldSymbol => "field",
        IEventSymbol => "event",
        _ => symbol.Kind.ToString().ToLowerInvariant(),
    };

    public static string? Accessibility(ISymbol symbol) => symbol.DeclaredAccessibility switch
    {
        Microsoft.CodeAnalysis.Accessibility.Public => "public",
        Microsoft.CodeAnalysis.Accessibility.Internal => "internal",
        Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
        Microsoft.CodeAnalysis.Accessibility.Private => "private",
        Microsoft.CodeAnalysis.Accessibility.ProtectedOrInternal => "protected internal",
        Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
        _ => null,
    };

    public static bool IsTopLevelStatements(ISymbol symbol) =>
        symbol is IMethodSymbol { Name: WellKnownMemberNames.TopLevelStatementsEntryPointMethodName };
}
