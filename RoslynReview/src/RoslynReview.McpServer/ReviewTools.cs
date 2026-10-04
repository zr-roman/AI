using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RoslynReview.Core.Diff;
using RoslynReview.Core.Navigation;
using RoslynReview.Core.Workspace;

namespace RoslynReview.McpServer;

/// <summary>
/// The tools the reviewing agent can call. All are read-only; descriptions are written for the model.
/// </summary>
internal sealed class ReviewTools(SolutionHost host)
{
    /// <summary>
    /// The SDK defaults plus relaxed escaping: C# signatures are full of generics, and "\u003C" for every "&lt;"
    /// costs the model tokens and readability. Tool output is never embedded in HTML.
    /// </summary>
    public static JsonSerializerOptions SerializerOptions { get; } =
        new(McpJsonUtilities.DefaultOptions) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [McpServerTool(Name = "get_changed_symbols", Title = "Symbols touched by a change",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Maps the change under review to the C# members and types it touches, using the compiler's view of the current source. " +
        "Pass baseRef to let the server diff the repository against a branch, or pass a unified diff. " +
        "For each changed symbol returns its id, kind, file and line range, whether it was added or modified and how many " +
        "lines changed. Files are listed with a status: deleted files, non-C# files and files outside the loaded solution " +
        "have no symbols. Members removed by the change are not listed individually; their containing type is reported as " +
        "modified. Start a review with this tool.")]
    public Task<ChangedSymbolsResult> GetChangedSymbolsAsync(
        [Description(
            "Branch, tag or commit the change will merge into, e.g. \"main\" or \"origin/main\". The server runs " +
            "`git diff --merge-base <baseRef>`, covering the branch's commits and uncommitted edits to tracked files.")]
        string? baseRef = null,
        [Description("A unified diff to analyze instead of baseRef, e.g. a pull request diff. Paths are relative to the repository root.")]
        string? diff = null,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(baseRef) == string.IsNullOrWhiteSpace(diff))
            {
                throw new McpException("Pass exactly one of baseRef (e.g. \"main\") or diff.");
            }

            var snapshot = await host.GetSnapshotAsync(cancellationToken);
            var unifiedDiff = string.IsNullOrWhiteSpace(diff)
                ? await GitDiff.AgainstAsync(snapshot.Paths.Root, baseRef!, cancellationToken)
                : diff;
            return await ChangedSymbolFinder.FindAsync(snapshot, unifiedDiff, cancellationToken);
        });

    [McpServerTool(Name = "get_symbol_source", Title = "Source of a symbol",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description(
        "Returns the source code of a method, property, field, event or type with line numbers, including its XML doc comment. " +
        "A type longer than maxLines is returned as an outline: its declaration plus one line per member with that member's id, " +
        "so you can request members one by one.")]
    public Task<string> GetSymbolSourceAsync(
        [Description("Symbol id exactly as returned by get_changed_symbols or find_callers, e.g. \"M:MyApp.OrderService.Place(MyApp.Order)\".")]
        string symbolId,
        [Description("Maximum number of lines to return, between 10 and 2000.")]
        int maxLines = SymbolSourceReader.DefaultMaxLines,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var snapshot = await host.GetSnapshotAsync(cancellationToken);
            var source = await SymbolSourceReader.ReadAsync(snapshot, symbolId, Math.Clamp(maxLines, 10, 2000), cancellationToken);
            return source.ToDisplayString();
        });

    [McpServerTool(Name = "find_callers", Title = "Callers and usages of a symbol",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false, UseStructuredContent = true)]
    [Description(
        "Finds every place in the solution that calls or references a symbol: method calls, property and field reads and writes, " +
        "usages of a type. Calls made through an interface or base member that the symbol implements or overrides are included " +
        "and marked with 'via'. Results are grouped by calling member and include the line of code at each call site. " +
        "Use it to judge the impact of changing a member.")]
    public Task<CallersResult> FindCallersAsync(
        [Description("Symbol id exactly as returned by get_changed_symbols or get_symbol_source.")]
        string symbolId,
        [Description("Maximum number of call sites to return, between 1 and 500.")]
        int maxResults = CallerFinder.DefaultMaxResults,
        CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            var snapshot = await host.GetSnapshotAsync(cancellationToken);
            return await CallerFinder.FindAsync(snapshot, symbolId, Math.Clamp(maxResults, 1, 500), cancellationToken);
        });

    // McpException messages reach the client as tool errors the model can act on;
    // any other exception is reported with a generic message.
    private static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is SymbolNotFoundException or SolutionLoadException or GitDiffException)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
