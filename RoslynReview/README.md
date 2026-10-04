# RoslynReview

An MCP server that gives AI agents compiler-accurate, read-only navigation of a C# solution for code review.
Built on Roslyn and the official [MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk).

A reviewer that only sees the diff cannot tell who calls the method that changed, which interface it implements
or what else a new parameter breaks. RoslynReview exposes that knowledge as MCP tools shaped around reviewing a
change, so an agent (or Claude Desktop, Claude Code, VS Code) can explore the code the way a human reviewer does.

> **Status:** week 1 of 3. The server loads a solution and serves the first three tools. Next: three more tools,
> then the review agent on Microsoft Agent Framework, then evals and packaging. See the [roadmap](#roadmap).

## Tools

| Tool | Input | Returns |
|------|-------|---------|
| `get_changed_symbols` | `baseRef` such as `main`, or a unified `diff` | The members and types the change touches: id, kind, `added` or `modified`, location, changed line count; a status for every file |
| `get_symbol_source` | symbol id, `maxLines` | Line-numbered source with its XML doc comment. Long types come back as an outline with member ids |
| `find_callers` | symbol id, `maxResults` | Call sites and usages grouped by calling member, with the line of code and `via` for calls through an interface or base member |

All tools are marked read-only (`readOnlyHint`). The solution is fixed when the server starts; no tool takes a file path.
Symbol ids are [documentation comment ids](https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/#id-strings):
stable, unambiguous across overloads, and accepted by the other tools as is.

`get_changed_symbols` on the test fixture (abridged):

```json
{
  "symbols": [
    {
      "id": "M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)",
      "name": "OrderService.PlaceOrderAsync(Order, CancellationToken)",
      "kind": "method", "change": "modified",
      "file": "src/SampleShop.Core/Orders/OrderService.cs", "startLine": 14, "endLine": 33,
      "changedLines": 5, "accessibility": "public"
    },
    {
      "id": "M:SampleShop.Orders.OrderService.Validate(SampleShop.Orders.Order)",
      "name": "OrderService.Validate(Order)",
      "kind": "method", "change": "added",
      "file": "src/SampleShop.Core/Orders/OrderService.cs", "startLine": 38, "endLine": 46,
      "changedLines": 8, "accessibility": "private"
    }
  ],
  "files": [
    { "path": "src/SampleShop.Core/Legacy/OldPricing.cs", "status": "deleted" },
    { "path": "src/SampleShop.Core/Orders/OrderService.cs", "status": "modified", "symbols": 4, "unmappedLines": 1 },
    { "path": "tools/Stamp.cs", "status": "not-in-solution" }
  ]
}
```

`get_symbol_source` for a type longer than `maxLines`:

```text
// class OrderService
// src/SampleShop.Core/Orders/OrderService.cs:5-47 (outline: 43 lines exceed maxLines=20; request a member by its id for full source)
 5 | /// <summary>Places orders: reserves stock, applies discounts and charges the customer.</summary>
 6 | public sealed class OrderService(IInventory inventory, IPaymentGateway payments) : IOrderService
 7 | {
 8 |     private readonly IInventory _inventory = inventory;  // F:SampleShop.Orders.OrderService._inventory
14 |     public async Task<OrderResult> PlaceOrderAsync(Order order, CancellationToken cancellationToken)  // M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)
35 |     public static decimal CalculateDiscountRate(Order order) =>  // M:SampleShop.Orders.OrderService.CalculateDiscountRate(SampleShop.Orders.Order)
47 | }
```

## Quick start

Requirements: .NET 10 SDK and git.

```bash
dotnet build
dotnet test
```

Run the server against a solution. Restore it first: Roslyn's `MSBuildWorkspace` needs restored projects to resolve references.

```bash
dotnet restore path/to/YourApp.slnx
dotnet src/RoslynReview.McpServer/bin/Debug/net10.0/RoslynReview.McpServer.dll --solution path/to/YourApp.slnx
```

| Option | Default |
|--------|---------|
| `--solution` (or `ROSLYN_REVIEW_SOLUTION`) | The only `.slnx`, `.sln` or `.csproj` in the working directory |
| `--repo-root` (or `ROSLYN_REVIEW_REPO_ROOT`) | The nearest directory above the solution that contains `.git` |

The server starts loading the solution in the background as soon as it starts, so the first tool call usually does not wait for MSBuild.

### MCP Inspector

`inspector.json` points at the test fixture:

```bash
npx @modelcontextprotocol/inspector --config inspector.json --server roslyn-review
```

### Claude Desktop

Add to `claude_desktop_config.json` (use absolute paths):

```json
{
  "mcpServers": {
    "roslyn-review": {
      "command": "dotnet",
      "args": [
        "C:\\src\\RoslynReview\\src\\RoslynReview.McpServer\\bin\\Debug\\net10.0\\RoslynReview.McpServer.dll",
        "--solution", "C:\\src\\YourApp\\YourApp.slnx"
      ]
    }
  }
}
```

Then ask something like: *"Review my branch against main. Start with get_changed_symbols, read what changed, and check the callers of every public member that changed."*

### Claude Code

```bash
claude mcp add roslyn-review -- dotnet /path/to/RoslynReview.McpServer.dll --solution /path/to/YourApp.slnx
```

### VS Code

`.vscode/mcp.json`:

```json
{
  "servers": {
    "roslyn-review": {
      "type": "stdio",
      "command": "dotnet",
      "args": ["/path/to/RoslynReview.McpServer.dll", "--solution", "${workspaceFolder}/YourApp.slnx"]
    }
  }
}
```

## How it works

```
src/RoslynReview.Core          Roslyn logic, no MCP dependency
  Workspace/SolutionHost       loads the solution once, caches it, re-reads edited files
  Diff/UnifiedDiffParser       git and plain unified diffs, renames, quoted paths, -U0
  Diff/GitDiff                 git diff --merge-base <baseRef> for the baseRef option
  Navigation/                  changed symbols, symbol source, callers
src/RoslynReview.McpServer     stdio host: configuration, warm-up, tool definitions
tests/RoslynReview.Tests       unit, workspace and end-to-end tests
tests/fixtures/SampleShop      two-project solution the tests run against
tests/fixtures/diffs           real git diffs against earlier versions of SampleShop
```

- **Loading.** `MSBuildWorkspace` runs a design-time build in a separate build-host process, so the server needs the
  .NET SDK but not `MSBuildLocator`. Snapshots are immutable; on each request files whose timestamp moved are re-read.
  Added or removed files and projects need a restart.
- **Mapping a diff to symbols.** Each added line belongs to the innermost member or type around it; doc comments and
  attributes belong to the member below. Removed code belongs to the declaration that encloses the gap it left, so
  deleting a statement marks its method and deleting a whole method marks its class. Whitespace-only changes are
  ignored, and members of a brand-new type are not listed separately.
- **`baseRef`.** The server runs `git diff --merge-base <baseRef>`: the branch's commits plus uncommitted edits,
  compared with exactly the files Roslyn reads. The ref is validated, passed after `--end-of-options`, and external
  diff drivers are disabled.
- **Callers.** `SymbolFinder.FindReferencesAsync` cascades to interface members and overrides; each usage is
  attributed to the member whose declaration contains it, signature included.
- **Output.** JSON tools return MCP structured content with an output schema. Source is returned as line-numbered text
  rather than JSON-escaped code. Logs go to stderr because stdout carries the protocol.

## Security

The tools only read, and the model cannot point them at arbitrary paths. Loading a solution, however, executes the
repository's MSBuild logic, as any build does. Only point the server at code you trust, or run it in a container
without secrets (planned for week 3).

## Roadmap

**Week 1: MCP server**
- [x] Solution loading with caching and refresh
- [x] `get_changed_symbols`, `get_symbol_source`, `find_callers`
- [x] Unit, workspace and end-to-end tests
- [ ] `get_implementations`, `get_new_diagnostics` (warnings introduced by the change), `search_code`
- [ ] MCP resources (team review rules, `.editorconfig`) and a `review_pr` prompt

**Week 2: review agent** on Microsoft Agent Framework: CLI, triage, four specialized reviewers, a verifier, deterministic
validation, inline GitHub comments.

**Week 3: quality and packaging:** evals on seeded bugs (diff-only vs diff + Roslyn), OpenTelemetry traces and cost,
container sandbox, NuGet package runnable with `dnx`.

## Limitations

- Members deleted by a change are not listed individually, only their containing type. Listing them needs the base
  revision, which comes with `get_new_diagnostics`.
- Untracked files are not part of `baseRef` diffs; `git add -N` them or pass a diff.
- C# only.
