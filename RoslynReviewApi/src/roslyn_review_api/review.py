"""Builds the review context: the workflow the MCP server recommends to agents, done in one request.

1. get_changed_symbols for the change.
2. get_symbol_source for each changed symbol.
3. find_callers for each modified member that code outside its type can call.
"""

from __future__ import annotations

import asyncio

from .models import ChangedSymbol, ReviewContext, ReviewContextRequest, SymbolContext
from .server import ReviewServer, ToolError


def needs_callers(symbol: ChangedSymbol) -> bool:
    """A modified member visible outside its type can break its callers; an added one has none yet,
    and the usages of a whole type are too broad to be useful here."""
    is_member = not symbol.id.startswith("T:")
    return is_member and symbol.change == "modified" and symbol.accessibility != "private"


async def build_review_context(
    server: ReviewServer, request: ReviewContextRequest, max_concurrency: int
) -> ReviewContext:
    changes = await server.changed_symbols(request)
    selected = changes.symbols[: request.max_symbols]
    limit = asyncio.Semaphore(max_concurrency)

    async def expand(symbol: ChangedSymbol) -> SymbolContext:
        context = SymbolContext(symbol=symbol)
        async with limit:
            try:
                context.source = await server.symbol_source(symbol.id, request.max_lines)
            except ToolError as ex:
                context.errors.append(f"source: {ex}")
        if needs_callers(symbol):
            async with limit:
                try:
                    context.callers = await server.find_callers(symbol.id, request.max_callers)
                except ToolError as ex:
                    context.errors.append(f"callers: {ex}")
        return context

    # A ToolError for one symbol is reported next to it; anything else (timeout, lost server)
    # cancels the remaining lookups and fails the request.
    try:
        async with asyncio.TaskGroup() as group:
            tasks = [group.create_task(expand(symbol)) for symbol in selected]
    except ExceptionGroup as failure:
        raise failure.exceptions[0] from None

    return ReviewContext(
        changes=changes,
        symbols=[task.result() for task in tasks],
        skipped_symbols=len(changes.symbols) - len(selected),
    )
