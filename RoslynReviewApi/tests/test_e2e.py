"""Against the real .NET server and the SampleShop fixture. CI builds the server and sets ROSLYN_REVIEW_E2E_SERVER.

    dotnet build RoslynReview/src/RoslynReview.McpServer
    dotnet restore RoslynReview/tests/fixtures/SampleShop/SampleShop.slnx
    ROSLYN_REVIEW_E2E_SERVER=RoslynReview/src/RoslynReview.McpServer/bin/Debug/net10.0/RoslynReview.McpServer.dll \
        uv run pytest -m e2e
"""

from __future__ import annotations

import os
from collections.abc import Iterator
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

from conftest import wait_until_ok
from roslyn_review_api import Settings, create_app

pytestmark = [
    pytest.mark.e2e,
    pytest.mark.skipif(not os.environ.get("ROSLYN_REVIEW_E2E_SERVER"), reason="ROSLYN_REVIEW_E2E_SERVER is not set"),
]

FIXTURES = Path(__file__).resolve().parents[2] / "RoslynReview" / "tests" / "fixtures"
SAMPLE_SHOP = FIXTURES / "SampleShop"
PLACE_ORDER = (
    "M:SampleShop.Orders.OrderService.PlaceOrderAsync(SampleShop.Orders.Order,System.Threading.CancellationToken)"
)
DISCOUNT_RATE = "M:SampleShop.Orders.OrderService.CalculateDiscountRate(SampleShop.Orders.Order)"


@pytest.fixture(scope="module")
def client() -> Iterator[TestClient]:
    settings = Settings.from_env(
        {
            "ROSLYN_REVIEW_SERVER": os.environ["ROSLYN_REVIEW_E2E_SERVER"],
            "ROSLYN_REVIEW_SOLUTION": str(SAMPLE_SHOP / "SampleShop.slnx"),
            "ROSLYN_REVIEW_REPO_ROOT": str(SAMPLE_SHOP),
        }
    )
    with TestClient(create_app(settings)) as test_client:
        wait_until_ok(test_client, timeout=60)
        yield test_client


def test_health_names_the_dotnet_server(client: TestClient) -> None:
    body = client.get("/health").json()

    assert body["status"] == "ok"
    assert body["server"].startswith("RoslynReview.McpServer")


def test_review_context_for_a_real_diff(client: TestClient) -> None:
    diff = (FIXTURES / "diffs" / "02-removals.diff").read_text()

    response = client.post("/v1/review-context", json={"diff": diff, "maxLines": 40})

    assert response.status_code == 200, response.text
    body = response.json()
    assert len(body["changes"]["symbols"]) == 3
    place = next(entry for entry in body["symbols"] if entry["symbol"]["id"] == PLACE_ORDER)
    assert place["symbol"]["change"] == "modified"
    assert place["symbol"]["changedLines"] == 1
    assert (
        "14 |     public async Task<OrderResult> PlaceOrderAsync(Order order, CancellationToken cancellationToken)"
        in (place["source"])
    )
    assert place["callers"]["totalSites"] > 0
    assert place["errors"] == []


def test_callers_keep_generic_signatures_readable(client: TestClient) -> None:
    response = client.get("/v1/symbols/callers", params={"id": DISCOUNT_RATE})

    assert response.status_code == 200, response.text
    names = [caller["name"] for caller in response.json()["callers"]]
    assert "OrderEndpoints.PreviewDiscount(IReadOnlyList<OrderLine>)" in names


def test_unknown_symbol_is_404(client: TestClient) -> None:
    response = client.get("/v1/symbols/source", params={"id": "M:SampleShop.Orders.OrderService.Nope"})

    assert response.status_code == 404
    assert "No symbol with id" in response.json()["detail"]


def test_missing_input_is_rejected_by_the_api(client: TestClient) -> None:
    assert client.post("/v1/changed-symbols", json={}).status_code == 422
