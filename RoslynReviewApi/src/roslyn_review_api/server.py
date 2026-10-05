"""A long-lived connection to the RoslynReview MCP server over stdio.

The server loads the solution once and caches it, so the API keeps one process for its whole lifetime
instead of starting one per request. A supervisor task owns the connection (anyio requires the task that
opened it to close it) and restarts the process with backoff if it exits.
"""

from __future__ import annotations

import asyncio
import json
import logging
from importlib.metadata import PackageNotFoundError, version
from typing import Any, Literal

import anyio
from mcp import Client, MCPError, StdioServerParameters
from mcp.types import CONNECTION_CLOSED, REQUEST_TIMEOUT, CallToolResult, Implementation, TextContent

from .models import CallersResult, ChangedSymbolsResult, ChangeRequest
from .settings import Settings

logger = logging.getLogger(__name__)

Status = Literal["starting", "ok", "unavailable"]


class ReviewServerError(Exception):
    """Base class for failures talking to the MCP server. Messages are safe to show to API clients."""


class ServerUnavailableError(ReviewServerError):
    """The server process is not running or not connected yet."""


class ServerTimeoutError(ReviewServerError):
    """A tool call took longer than the configured timeout."""


class ProtocolError(ReviewServerError):
    """The server answered with an MCP protocol error or an unexpected result shape."""


class ToolError(ReviewServerError):
    """The tool ran and rejected the request, e.g. an unknown symbol id or a bad ref.

    The message comes from the server and is written to be acted on.
    """

    @property
    def symbol_not_found(self) -> bool:
        # SymbolNotFoundException in RoslynReview.Core/Navigation/SymbolResolver.cs.
        message = str(self)
        return message.startswith("No symbol with id") or "is not a symbol id" in message


def _client_info() -> Implementation:
    try:
        package_version = version("roslyn-review-api")
    except PackageNotFoundError:
        package_version = "0.0.0"
    return Implementation(name="roslyn-review-api", version=package_version)


class ReviewServer:
    """Typed access to the three RoslynReview tools over one supervised MCP connection."""

    def __init__(self, settings: Settings, *, max_restart_delay: float = 30.0, ready_wait: float = 10.0) -> None:
        self._settings = settings
        self._max_restart_delay = max_restart_delay
        self._ready_wait = ready_wait
        self._client: Client | None = None
        self._ready = asyncio.Event()
        self._disconnect = asyncio.Event()
        self._stopping = False
        self._task: asyncio.Task[None] | None = None
        self._ever_connected = False
        self.server_info: str | None = None
        self.last_error: str | None = None

    @property
    def status(self) -> Status:
        if self._client is not None:
            return "ok"
        return "unavailable" if self.last_error or self._ever_connected else "starting"

    async def start(self) -> None:
        self._task = asyncio.create_task(self._supervise(), name="roslyn-review-mcp")

    async def stop(self) -> None:
        self._stopping = True
        self._disconnect.set()
        if self._task is not None:
            await self._task

    async def wait_ready(self, seconds: float) -> bool:
        try:
            await asyncio.wait_for(self._ready.wait(), seconds)
        except TimeoutError:
            return False
        return True

    # --- Tools -------------------------------------------------------------------------------

    async def changed_symbols(self, change: ChangeRequest) -> ChangedSymbolsResult:
        result = await self._call("get_changed_symbols", change.tool_arguments())
        return ChangedSymbolsResult.model_validate(_structured(result))

    async def symbol_source(self, symbol_id: str, max_lines: int) -> str:
        result = await self._call("get_symbol_source", {"symbolId": symbol_id, "maxLines": max_lines})
        return _text(result)

    async def find_callers(self, symbol_id: str, max_results: int) -> CallersResult:
        result = await self._call("find_callers", {"symbolId": symbol_id, "maxResults": max_results})
        return CallersResult.model_validate(_structured(result))

    async def _call(self, name: str, arguments: dict[str, Any]) -> CallToolResult:
        client = self._client
        if client is None and await self.wait_ready(self._ready_wait):
            client = self._client
        if client is None:
            reason = f": {self.last_error}" if self.last_error else ""
            raise ServerUnavailableError(f"RoslynReview server is not connected{reason}")

        try:
            result = await client.call_tool(name, arguments, read_timeout_seconds=self._settings.tool_timeout)
        except TimeoutError:
            raise ServerTimeoutError(f"{name} did not finish in {self._settings.tool_timeout:g} s") from None
        except MCPError as ex:
            if ex.code == REQUEST_TIMEOUT:
                raise ServerTimeoutError(f"{name} did not finish in {self._settings.tool_timeout:g} s") from None
            if ex.code == CONNECTION_CLOSED:
                self._connection_lost(client, ex.message)
                raise ServerUnavailableError(f"RoslynReview server disconnected: {ex.message}") from None
            raise ProtocolError(f"{name} failed: {ex.message}") from None
        except (anyio.ClosedResourceError, anyio.BrokenResourceError, anyio.EndOfStream) as ex:
            # The stdio streams raise these when the process has exited.
            self._connection_lost(client, repr(ex))
            raise ServerUnavailableError(f"RoslynReview server disconnected: {ex!r}") from None

        if result.is_error:
            raise ToolError(_text(result) or f"{name} failed")
        return result

    # --- Connection --------------------------------------------------------------------------

    def _connection_lost(self, client: Client, reason: str) -> None:
        if self._client is client:
            logger.warning("Lost the RoslynReview server: %s", reason)
            self.last_error = reason
            self._disconnect.set()

    async def _supervise(self) -> None:
        settings = self._settings
        parameters = StdioServerParameters(
            command=settings.command,
            args=list(settings.args),
            cwd=settings.cwd,
            env=dict(settings.env) or None,
        )
        delay = 1.0
        while not self._stopping:
            self._disconnect.clear()
            try:
                # 'legacy' is the initialize handshake every MCP server speaks, the .NET SDK included.
                async with Client(parameters, mode="legacy", client_info=_client_info()) as client:
                    info = client.server_info
                    self.server_info = f"{info.name} {info.version}".strip() if info else None
                    self.last_error = None
                    self._client = client
                    self._ever_connected = True
                    self._ready.set()
                    delay = 1.0
                    logger.info("Connected to %s", self.server_info or settings.command)
                    await self._disconnect.wait()
            except Exception as ex:
                self.last_error = f"{type(ex).__name__}: {ex}" if str(ex) else type(ex).__name__
                logger.error("RoslynReview server failed: %s", self.last_error)
            finally:
                self._client = None
                self._ready.clear()

            if self._stopping:
                break
            logger.info("Restarting the RoslynReview server in %.0f s", delay)
            try:
                await asyncio.wait_for(self._wait_for_stop(), delay)
            except TimeoutError:
                pass
            delay = min(delay * 2, self._max_restart_delay)

    async def _wait_for_stop(self) -> None:
        while not self._stopping:
            await self._disconnect.wait()
            self._disconnect.clear()


def _text(result: CallToolResult) -> str:
    return "\n".join(block.text for block in result.content if isinstance(block, TextContent))


def _structured(result: CallToolResult) -> Any:
    if result.structured_content is not None:
        return result.structured_content
    # Servers that predate structured content send the same JSON as text.
    try:
        return json.loads(_text(result))
    except json.JSONDecodeError as ex:
        raise ProtocolError(f"Expected JSON from the server, got: {_text(result)[:200]!r}") from ex
