"""Request and response bodies. Field names are camelCase on the wire, matching the MCP server's JSON."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator
from pydantic.alias_generators import to_camel


class ApiModel(BaseModel):
    model_config = ConfigDict(alias_generator=to_camel, populate_by_name=True)


# --- What the MCP server returns -------------------------------------------------------------


class ChangedSymbol(ApiModel):
    id: str = Field(description="Documentation comment id, accepted as is by the other endpoints.")
    name: str
    kind: str = Field(description="method, property, class, constructor, ...")
    change: Literal["added", "modified"]
    file: str = Field(description="Path relative to the repository root.")
    start_line: int
    end_line: int
    changed_lines: int
    accessibility: str | None = None


class ChangedFile(ApiModel):
    path: str
    status: str = Field(description="added, modified, deleted, renamed, binary, not-csharp or not-in-solution.")
    old_path: str | None = None
    symbols: int | None = None
    unmapped_lines: int | None = Field(None, description="Changed lines outside any member, e.g. using directives.")


class ChangedSymbolsResult(ApiModel):
    symbols: list[ChangedSymbol]
    files: list[ChangedFile]


class CallSite(ApiModel):
    file: str
    line: int
    code: str
    via: str | None = Field(None, description="Interface or base member the call goes through.")
    implicit: bool | None = None


class Caller(ApiModel):
    id: str
    name: str
    kind: str
    project: str
    sites: list[CallSite]


class CallersResult(ApiModel):
    id: str
    name: str
    total_sites: int
    truncated: bool
    callers: list[Caller]


# --- Requests --------------------------------------------------------------------------------


class ChangeRequest(ApiModel):
    """The change under review: a branch to diff against, or a ready unified diff."""

    base_ref: str | None = Field(
        None,
        description='Branch, tag or commit the change merges into, e.g. "main". '
        "The server runs `git diff --merge-base <baseRef>` in the repository.",
        examples=["main"],
    )
    diff: str | None = Field(
        None, description="Unified diff, e.g. a pull request diff. Paths relative to the repo root."
    )

    @model_validator(mode="after")
    def _exactly_one(self) -> ChangeRequest:
        if bool(self.base_ref and self.base_ref.strip()) == bool(self.diff and self.diff.strip()):
            raise ValueError('Pass exactly one of baseRef (e.g. "main") or diff.')
        return self

    def tool_arguments(self) -> dict[str, str]:
        return {"baseRef": self.base_ref} if self.base_ref else {"diff": self.diff or ""}


class ReviewContextRequest(ChangeRequest):
    max_symbols: int = Field(20, ge=1, le=100, description="Changed symbols to expand; the rest are only counted.")
    max_lines: int = Field(150, ge=10, le=2000, description="Source lines per symbol; longer types become an outline.")
    max_callers: int = Field(20, ge=1, le=500, description="Call sites per symbol.")


# --- /v1/review-context ----------------------------------------------------------------------


class SymbolContext(ApiModel):
    symbol: ChangedSymbol
    source: str | None = Field(None, description="Line-numbered source, as get_symbol_source returns it.")
    callers: CallersResult | None = Field(
        None, description="Only for modified members visible outside their type: those are the ones a change can break."
    )
    errors: list[str] = Field(default_factory=list, description="Lookups that failed for this symbol.")


class ReviewContext(ApiModel):
    """Everything a reviewer needs in one response: what changed, its source, and who depends on it."""

    changes: ChangedSymbolsResult
    symbols: list[SymbolContext]
    skipped_symbols: int = Field(description="Changed symbols beyond maxSymbols, listed in changes but not expanded.")


class Health(ApiModel):
    status: Literal["ok", "starting", "unavailable"]
    server: str | None = Field(None, description="Name and version the MCP server reported.")
    solution: str | None = None
    error: str | None = Field(None, description="Why the last connection attempt failed.")


class Problem(ApiModel):
    detail: str
