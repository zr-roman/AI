from __future__ import annotations

from pathlib import Path

import pytest

from roslyn_review_api.settings import Settings, SettingsError


def test_dll_is_started_with_dotnet() -> None:
    settings = Settings.from_env(
        {
            "ROSLYN_REVIEW_SERVER": "/opt/rr/RoslynReview.McpServer.dll",
            "ROSLYN_REVIEW_SOLUTION": "/src/App/App.slnx",
            "ROSLYN_REVIEW_REPO_ROOT": "/src/App",
        }
    )

    assert settings.command == "dotnet"
    assert settings.args == (
        "/opt/rr/RoslynReview.McpServer.dll",
        "--solution",
        "/src/App/App.slnx",
        "--repo-root",
        "/src/App",
    )
    assert settings.solution == "/src/App/App.slnx"
    assert settings.api_key is None


def test_executable_is_started_directly_and_relative_solution_is_resolved() -> None:
    settings = Settings.from_env(
        {
            "ROSLYN_REVIEW_SERVER": "/opt/rr/RoslynReview.McpServer",
            "ROSLYN_REVIEW_SOLUTION": "App.slnx",
            "ROSLYN_REVIEW_TOOL_TIMEOUT": "12.5",
            "ROSLYN_REVIEW_API_KEY": "k",
        }
    )

    assert settings.command == "/opt/rr/RoslynReview.McpServer"
    assert settings.args == ("--solution", str(Path("App.slnx").resolve()))
    assert settings.tool_timeout == 12.5
    assert settings.api_key == "k"


@pytest.mark.parametrize(
    ("environ", "message"),
    [
        ({}, "ROSLYN_REVIEW_SERVER"),
        ({"ROSLYN_REVIEW_SERVER": "x.dll"}, "ROSLYN_REVIEW_SOLUTION"),
        (
            {"ROSLYN_REVIEW_SERVER": "x.dll", "ROSLYN_REVIEW_SOLUTION": "a.slnx", "ROSLYN_REVIEW_TOOL_TIMEOUT": "soon"},
            "must be a number",
        ),
        (
            {"ROSLYN_REVIEW_SERVER": "x.dll", "ROSLYN_REVIEW_SOLUTION": "a.slnx", "ROSLYN_REVIEW_MAX_CONCURRENCY": "0"},
            "must be positive",
        ),
    ],
)
def test_invalid_settings(environ: dict[str, str], message: str) -> None:
    with pytest.raises(SettingsError, match=message):
        Settings.from_env(environ)
