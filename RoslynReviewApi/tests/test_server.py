"""Connection lifecycle: timeouts, a server that exits, a server that never starts, API keys."""

from __future__ import annotations

import time

from fastapi.testclient import TestClient

from conftest import fake_settings, wait_until_ok
from fake_server import PLACE_ORDER
from roslyn_review_api import create_app
from roslyn_review_api.server import ReviewServer


def test_api_key_is_required_when_configured() -> None:
    with TestClient(create_app(fake_settings(api_key="s3cret"))) as client:
        wait_until_ok(client)

        assert client.get("/v1/symbols/source", params={"id": PLACE_ORDER}).status_code == 401
        wrong = client.get("/v1/symbols/source", params={"id": PLACE_ORDER}, headers={"X-API-Key": "nope"})
        assert wrong.status_code == 401
        ok = client.get("/v1/symbols/source", params={"id": PLACE_ORDER}, headers={"X-API-Key": "s3cret"})
        assert ok.status_code == 200
        assert client.get("/health").status_code == 200


def test_slow_tool_call_times_out_with_504() -> None:
    with TestClient(create_app(fake_settings(tool_timeout=0.5))) as client:
        wait_until_ok(client)

        response = client.post("/v1/changed-symbols", json={"baseRef": "slow"})

        assert response.status_code == 504
        assert "did not finish in 0.5 s" in response.json()["detail"]


def test_server_is_restarted_after_it_exits() -> None:
    settings = fake_settings()
    server = ReviewServer(settings, max_restart_delay=0.2)
    with TestClient(create_app(settings, server)) as client:
        wait_until_ok(client)

        crashed = client.get("/v1/symbols/source", params={"id": "M:Crash"})
        assert crashed.status_code == 503
        assert "disconnected" in crashed.json()["detail"]

        wait_until_ok(client)
        assert client.get("/v1/symbols/source", params={"id": PLACE_ORDER}).status_code == 200


def test_server_that_cannot_start_is_reported_as_unavailable() -> None:
    settings = fake_settings(command="/nonexistent/RoslynReview.McpServer")
    with TestClient(create_app(settings, ReviewServer(settings, ready_wait=0.5))) as client:
        deadline = time.monotonic() + 10
        while client.get("/health").json()["status"] == "starting" and time.monotonic() < deadline:
            time.sleep(0.05)

        health = client.get("/health")
        assert health.status_code == 503
        assert health.json()["status"] == "unavailable"
        assert health.json()["error"]

        response = client.post("/v1/changed-symbols", json={"baseRef": "main"})
        assert response.status_code == 503
        assert response.json()["detail"].startswith("RoslynReview server is not connected")
