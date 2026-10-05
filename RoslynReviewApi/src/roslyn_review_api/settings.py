"""Startup settings, read from environment variables."""

from __future__ import annotations

import os
from collections.abc import Mapping
from dataclasses import dataclass, field
from pathlib import Path


class SettingsError(Exception):
    """A required setting is missing or invalid. The message says which one."""


@dataclass(frozen=True)
class Settings:
    """How to start the RoslynReview MCP server and how to serve HTTP.

    The solution is fixed for the lifetime of the process, as in the MCP server itself:
    no endpoint accepts a file path.
    """

    command: str
    """Executable that starts the MCP server, e.g. ``dotnet``."""

    args: tuple[str, ...] = ()
    """Arguments for ``command``, including ``--solution`` and ``--repo-root``."""

    cwd: Path | None = None
    env: Mapping[str, str] = field(default_factory=dict)

    api_key: str | None = None
    """When set, every /v1 request must carry it in the ``X-API-Key`` header."""

    tool_timeout: float = 300.0
    """Seconds one tool call may take. The first call can wait for MSBuild to load the solution."""

    max_concurrency: int = 4
    """Tool calls /v1/review-context runs at the same time."""

    @property
    def solution(self) -> str | None:
        return _option(self.args, "--solution")

    @classmethod
    def from_env(cls, environ: Mapping[str, str] = os.environ) -> Settings:
        """Reads ``ROSLYN_REVIEW_*`` variables.

        ``ROSLYN_REVIEW_SERVER`` is the path to ``RoslynReview.McpServer.dll`` (started with ``dotnet``)
        or to a native executable. ``ROSLYN_REVIEW_SOLUTION`` and ``ROSLYN_REVIEW_REPO_ROOT`` are passed
        through as ``--solution`` and ``--repo-root``.
        """
        server = environ.get("ROSLYN_REVIEW_SERVER")
        if not server:
            raise SettingsError(
                "Set ROSLYN_REVIEW_SERVER to the path of RoslynReview.McpServer.dll "
                "(build it with `dotnet build` in RoslynReview/)."
            )
        solution = environ.get("ROSLYN_REVIEW_SOLUTION")
        if not solution:
            raise SettingsError("Set ROSLYN_REVIEW_SOLUTION to the .slnx, .sln or .csproj under review.")

        command, args = ("dotnet", [server]) if server.lower().endswith(".dll") else (server, [])
        args += ["--solution", str(Path(solution).resolve())]
        if repo_root := environ.get("ROSLYN_REVIEW_REPO_ROOT"):
            args += ["--repo-root", str(Path(repo_root).resolve())]

        return cls(
            command=command,
            args=tuple(args),
            api_key=environ.get("ROSLYN_REVIEW_API_KEY") or None,
            tool_timeout=_positive(environ, "ROSLYN_REVIEW_TOOL_TIMEOUT", 300.0),
            max_concurrency=max(1, int(_positive(environ, "ROSLYN_REVIEW_MAX_CONCURRENCY", 4))),
        )


def _option(args: tuple[str, ...], name: str) -> str | None:
    for i, arg in enumerate(args[:-1]):
        if arg == name:
            return args[i + 1]
    return None


def _positive(environ: Mapping[str, str], name: str, default: float) -> float:
    raw = environ.get(name)
    if not raw:
        return default
    try:
        value = float(raw)
    except ValueError:
        raise SettingsError(f"{name} must be a number, got {raw!r}.") from None
    if value <= 0:
        raise SettingsError(f"{name} must be positive, got {raw!r}.")
    return value
