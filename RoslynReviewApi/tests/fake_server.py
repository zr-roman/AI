"""A stand-in for the .NET RoslynReview MCP server: same tool names, arguments, result shapes and error texts.

Run over stdio by the tests, so the API is exercised through a real subprocess and the real MCP transport
without the .NET SDK. Data mirrors the SampleShop fixture in RoslynReview/tests/fixtures.
"""

from __future__ import annotations

import functools
import os
import time
from typing import Any

from mcp.server.mcpserver import MCPServer
from mcp.types import CallToolResult, TextContent


class ToolError(Exception):
    """Reported like the .NET server reports McpException: isError with the bare message."""


def _tool_errors(function):
    @functools.wraps(function)
    def wrapper(*args: Any, **kwargs: Any) -> Any:
        try:
            return function(*args, **kwargs)
        except ToolError as ex:
            return CallToolResult(content=[TextContent(type="text", text=str(ex))], is_error=True)

    return wrapper


server = MCPServer("RoslynReview.McpServer (fake)")

PLACE_ORDER = (
    "M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)"
)
VALIDATE = "M:SampleShop.Orders.OrderService.Validate(SampleShop.Orders.Order)"
ORDER_SERVICE = "T:SampleShop.Orders.OrderService"
BROKEN = "M:SampleShop.Orders.OrderService.Broken"

SYMBOLS: list[dict[str, Any]] = [
    {
        "id": ORDER_SERVICE,
        "name": "OrderService",
        "kind": "class",
        "change": "modified",
        "file": "src/SampleShop.Core/Orders/OrderService.cs",
        "startLine": 5,
        "endLine": 47,
        "changedLines": 1,
        "accessibility": "public",
    },
    {
        "id": PLACE_ORDER,
        "name": "OrderService.PlaceOrderAsync(Order, CancellationToken)",
        "kind": "method",
        "change": "modified",
        "file": "src/SampleShop.Core/Orders/OrderService.cs",
        "startLine": 14,
        "endLine": 33,
        "changedLines": 5,
        "accessibility": "public",
    },
    {
        "id": VALIDATE,
        "name": "OrderService.Validate(Order)",
        "kind": "method",
        "change": "added",
        "file": "src/SampleShop.Core/Orders/OrderService.cs",
        "startLine": 38,
        "endLine": 46,
        "changedLines": 8,
        "accessibility": "private",
    },
    {
        "id": BROKEN,
        "name": "OrderService.Broken",
        "kind": "method",
        "change": "modified",
        "file": "src/SampleShop.Core/Orders/OrderService.cs",
        "startLine": 40,
        "endLine": 41,
        "changedLines": 1,
        "accessibility": "internal",
    },
]

FILES: list[dict[str, Any]] = [
    {"path": "src/SampleShop.Core/Legacy/OldPricing.cs", "status": "deleted"},
    {"path": "src/SampleShop.Core/Orders/OrderService.cs", "status": "modified", "symbols": 4, "unmappedLines": 1},
    {"path": "tools/Stamp.cs", "status": "not-in-solution"},
]


def _not_found(symbol_id: str) -> ToolError:
    return ToolError(
        f"No symbol with id '{symbol_id}' is declared in the solution. Ids are case-sensitive and include "
        "parameter types; symbols from NuGet packages or the framework have no source here."
    )


@server.tool(name="get_changed_symbols")
@_tool_errors
def get_changed_symbols(baseRef: str | None = None, diff: str | None = None) -> dict[str, Any]:
    if bool(baseRef) == bool(diff):
        raise ToolError('Pass exactly one of baseRef (e.g. "main") or diff.')
    if baseRef == "no-such-branch":
        raise ToolError("git diff failed: fatal: bad revision 'no-such-branch'")
    if baseRef == "slow":
        time.sleep(5)
    symbols = SYMBOLS if baseRef else SYMBOLS[1:3]
    return {"symbols": symbols, "files": FILES}


@server.tool(name="get_symbol_source")
@_tool_errors
def get_symbol_source(symbolId: str, maxLines: int = 150) -> str:
    if symbolId == "M:Crash":
        os._exit(1)
    if not any(s["id"] == symbolId for s in SYMBOLS) or symbolId == BROKEN:
        raise _not_found(symbolId)
    return f"// {symbolId}\n// maxLines={maxLines}\n14 | public async Task<OrderResult> PlaceOrderAsync(...)"


@server.tool(name="find_callers")
@_tool_errors
def find_callers(symbolId: str, maxResults: int = 50) -> dict[str, Any]:
    if symbolId != PLACE_ORDER:
        raise _not_found(symbolId)
    sites = [
        {
            "file": "src/SampleShop.Api/OrderEndpoints.cs",
            "line": 12,
            "code": "await orders.PlaceOrderAsync(o, ct);",
            "via": PLACE_ORDER.replace("OrderService.", "IOrderService."),
        },
    ]
    return {
        "id": symbolId,
        "name": "OrderService.PlaceOrderAsync(Order, CancellationToken)",
        "totalSites": 1,
        "truncated": maxResults < 1,
        "callers": [
            {
                "id": "M:SampleShop.Api.OrderEndpoints.Place",
                "name": "OrderEndpoints.Place",
                "kind": "method",
                "project": "SampleShop.Api",
                "sites": sites,
            }
        ],
    }


if __name__ == "__main__":
    server.run("stdio")
