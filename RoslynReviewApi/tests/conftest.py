from __future__ import annotations

import sys
import time
from collections.abc import Iterator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from roslyn_review_api import Settings, create_app

FAKE_SERVER = Path(__file__).with_name("fake_server.py")


def fake_settings(**overrides: object) -> Settings:
    values: dict[str, object] = {
        "command": sys.executable,
        "args": (str(FAKE_SERVER), "--solution", "/repo/SampleShop.slnx"),
        "tool_timeout": 30.0,
    }
    values.update(overrides)
    return Settings(**values)  # type: ignore[arg-type]


def wait_until_ok(client: TestClient, timeout: float = 30.0) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if client.get("/health").status_code == 200:
            return
        time.sleep(0.1)
    raise AssertionError(f"server never became ready: {client.get('/health').json()}")


@pytest.fixture(scope="module")
def client() -> Iterator[TestClient]:
    with TestClient(create_app(fake_settings())) as test_client:
        wait_until_ok(test_client)
        yield test_client
