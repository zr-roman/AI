"""FastAPI application: REST endpoints over the RoslynReview MCP tools."""

from __future__ import annotations

import secrets
from collections.abc import AsyncIterator
from contextlib import asynccontextmanager
from typing import Annotated

from fastapi import APIRouter, Depends, FastAPI, HTTPException, Query, Request, Security, status
from fastapi.responses import JSONResponse, PlainTextResponse
from fastapi.security import APIKeyHeader

from .models import (
    CallersResult,
    ChangedSymbolsResult,
    ChangeRequest,
    Health,
    Problem,
    ReviewContext,
    ReviewContextRequest,
)
from .review import build_review_context
from .server import (
    ProtocolError,
    ReviewServer,
    ReviewServerError,
    ServerTimeoutError,
    ServerUnavailableError,
    ToolError,
)
from .settings import Settings

SymbolId = Annotated[
    str,
    Query(
        alias="id",
        min_length=3,
        description="Documentation comment id exactly as another endpoint returned it.",
        examples=["M:SampleShop.Orders.OrderService.CalculateDiscountRate(SampleShop.Orders.Order)"],
    ),
]

_api_key_header = APIKeyHeader(name="X-API-Key", auto_error=False)


def get_server(request: Request) -> ReviewServer:
    return request.app.state.review_server


def require_api_key(request: Request, api_key: Annotated[str | None, Security(_api_key_header)]) -> None:
    expected = request.app.state.settings.api_key
    if expected and not (api_key and secrets.compare_digest(api_key.encode(), expected.encode())):
        raise HTTPException(status.HTTP_401_UNAUTHORIZED, "Missing or wrong X-API-Key header.")


Server = Annotated[ReviewServer, Depends(get_server)]

_errors = {
    status.HTTP_401_UNAUTHORIZED: {"model": Problem, "description": "Missing or wrong X-API-Key."},
    status.HTTP_422_UNPROCESSABLE_CONTENT: {"model": Problem, "description": "The tool rejected the request."},
    status.HTTP_502_BAD_GATEWAY: {"model": Problem, "description": "The MCP server answered with an error."},
    status.HTTP_503_SERVICE_UNAVAILABLE: {"model": Problem, "description": "The MCP server is not running."},
    status.HTTP_504_GATEWAY_TIMEOUT: {"model": Problem, "description": "The tool call timed out."},
}

v1 = APIRouter(prefix="/v1", dependencies=[Depends(require_api_key)], responses=_errors)


@v1.post("/changed-symbols", tags=["review"], summary="Symbols touched by a change")
async def changed_symbols(change: ChangeRequest, server: Server) -> ChangedSymbolsResult:
    """Maps a change (`baseRef` or a unified `diff`) to the C# members and types it touches."""
    return await server.changed_symbols(change)


@v1.get(
    "/symbols/source",
    tags=["navigation"],
    summary="Source of a symbol",
    response_class=PlainTextResponse,
    responses={200: {"content": {"text/plain": {}}}, 404: {"model": Problem, "description": "Unknown symbol id."}},
)
async def symbol_source(
    symbol_id: SymbolId,
    server: Server,
    max_lines: Annotated[int, Query(alias="maxLines", ge=10, le=2000)] = 150,
) -> PlainTextResponse:
    """Line-numbered source with its XML doc comment. A type longer than `maxLines` comes back as an outline."""
    return PlainTextResponse(await server.symbol_source(symbol_id, max_lines))


@v1.get(
    "/symbols/callers",
    tags=["navigation"],
    summary="Callers and usages of a symbol",
    responses={404: {"model": Problem, "description": "Unknown symbol id."}},
)
async def find_callers(
    symbol_id: SymbolId,
    server: Server,
    max_results: Annotated[int, Query(alias="maxResults", ge=1, le=500)] = 50,
) -> CallersResult:
    """Call sites grouped by calling member; calls through an interface or base member carry `via`."""
    return await server.find_callers(symbol_id, max_results)


@v1.post("/review-context", tags=["review"], summary="Everything a reviewer needs for a change")
async def review_context(body: ReviewContextRequest, request: Request, server: Server) -> ReviewContext:
    """Changed symbols, the source of each, and the callers of every modified non-private member, in one call.

    Meant as the input of an LLM reviewer that does not speak MCP.
    """
    return await build_review_context(server, body, request.app.state.settings.max_concurrency)


def create_app(settings: Settings, server: ReviewServer | None = None) -> FastAPI:
    review_server = server or ReviewServer(settings)

    @asynccontextmanager
    async def lifespan(app: FastAPI) -> AsyncIterator[None]:
        await review_server.start()
        try:
            yield
        finally:
            await review_server.stop()

    app = FastAPI(
        title="RoslynReview API",
        version="0.1.0",
        summary="Compiler-accurate C# navigation for code review, over HTTP.",
        description="REST facade for the RoslynReview MCP server. The solution under review is fixed at startup.",
        lifespan=lifespan,
    )
    app.state.settings = settings
    app.state.review_server = review_server

    @app.get("/health", tags=["service"], responses={503: {"model": Health}})
    async def health() -> JSONResponse:
        """`ok` once the MCP server is connected. The solution itself loads in the background after that."""
        body = Health(
            status=review_server.status,
            server=review_server.server_info,
            solution=settings.solution,
            error=review_server.last_error,
        )
        code = status.HTTP_200_OK if body.status == "ok" else status.HTTP_503_SERVICE_UNAVAILABLE
        return JSONResponse(body.model_dump(by_alias=True, exclude_none=True), status_code=code)

    app.include_router(v1)

    @app.exception_handler(ReviewServerError)
    async def server_error(_: Request, ex: ReviewServerError) -> JSONResponse:
        return JSONResponse({"detail": str(ex)}, status_code=_status_code(ex))

    return app


def _status_code(ex: ReviewServerError) -> int:
    match ex:
        case ToolError() if ex.symbol_not_found:
            return status.HTTP_404_NOT_FOUND
        case ToolError():
            return status.HTTP_422_UNPROCESSABLE_CONTENT
        case ServerTimeoutError():
            return status.HTTP_504_GATEWAY_TIMEOUT
        case ServerUnavailableError():
            return status.HTTP_503_SERVICE_UNAVAILABLE
        case ProtocolError():
            return status.HTTP_502_BAD_GATEWAY
        case _:
            return status.HTTP_500_INTERNAL_SERVER_ERROR
