from __future__ import annotations

from fastapi.testclient import TestClient

from fake_server import BROKEN, ORDER_SERVICE, PLACE_ORDER, VALIDATE


def test_health_reports_server_and_solution(client: TestClient) -> None:
    body = client.get("/health").json()

    assert body["status"] == "ok"
    assert body["server"].startswith("RoslynReview.McpServer (fake)")
    assert body["solution"] == "/repo/SampleShop.slnx"


def test_changed_symbols_by_base_ref(client: TestClient) -> None:
    response = client.post("/v1/changed-symbols", json={"baseRef": "main"})

    assert response.status_code == 200
    body = response.json()
    place = next(s for s in body["symbols"] if s["id"] == PLACE_ORDER)
    assert place == {
        "id": PLACE_ORDER,
        "name": "OrderService.PlaceOrderAsync(Order, CancellationToken)",
        "kind": "method",
        "change": "modified",
        "file": "src/SampleShop.Core/Orders/OrderService.cs",
        "startLine": 14,
        "endLine": 33,
        "changedLines": 5,
        "accessibility": "public",
    }
    assert {
        "path": "tools/Stamp.cs",
        "status": "not-in-solution",
        "oldPath": None,
        "symbols": None,
        "unmappedLines": None,
    } in body["files"]


def test_changed_symbols_by_diff(client: TestClient) -> None:
    response = client.post("/v1/changed-symbols", json={"diff": "diff --git a/x.cs b/x.cs\n"})

    assert response.status_code == 200
    assert [s["id"] for s in response.json()["symbols"]] == [PLACE_ORDER, VALIDATE]


def test_changed_symbols_needs_exactly_one_input(client: TestClient) -> None:
    for body in ({}, {"baseRef": "main", "diff": "x"}, {"baseRef": "  "}):
        response = client.post("/v1/changed-symbols", json=body)
        assert response.status_code == 422, body
        assert "exactly one of baseRef" in response.text


def test_tool_error_is_422_with_server_message(client: TestClient) -> None:
    response = client.post("/v1/changed-symbols", json={"baseRef": "no-such-branch"})

    assert response.status_code == 422
    assert response.json() == {"detail": "git diff failed: fatal: bad revision 'no-such-branch'"}


def test_symbol_source_is_plain_text(client: TestClient) -> None:
    response = client.get("/v1/symbols/source", params={"id": PLACE_ORDER, "maxLines": 40})

    assert response.status_code == 200
    assert response.headers["content-type"].startswith("text/plain")
    assert response.text.splitlines()[:2] == [f"// {PLACE_ORDER}", "// maxLines=40"]


def test_unknown_symbol_is_404(client: TestClient) -> None:
    response = client.get("/v1/symbols/source", params={"id": "M:Nope.Nothing"})

    assert response.status_code == 404
    assert response.json()["detail"].startswith("No symbol with id 'M:Nope.Nothing'")


def test_query_limits_are_validated_before_calling_the_server(client: TestClient) -> None:
    assert client.get("/v1/symbols/source", params={"id": PLACE_ORDER, "maxLines": 5}).status_code == 422
    assert client.get("/v1/symbols/callers", params={"id": PLACE_ORDER, "maxResults": 501}).status_code == 422
    assert client.get("/v1/symbols/callers").status_code == 422


def test_find_callers(client: TestClient) -> None:
    response = client.get("/v1/symbols/callers", params={"id": PLACE_ORDER})

    assert response.status_code == 200
    body = response.json()
    assert body["totalSites"] == 1
    assert body["truncated"] is False
    site = body["callers"][0]["sites"][0]
    assert site["line"] == 12
    assert site["via"].startswith("M:SampleShop.Orders.IOrderService.PlaceOrderAsync")


def test_review_context_expands_symbols_and_callers(client: TestClient) -> None:
    response = client.post("/v1/review-context", json={"baseRef": "main", "maxLines": 60})

    assert response.status_code == 200
    body = response.json()
    assert body["skippedSymbols"] == 0
    by_id = {entry["symbol"]["id"]: entry for entry in body["symbols"]}
    assert list(by_id) == [ORDER_SERVICE, PLACE_ORDER, VALIDATE, BROKEN]

    # Modified public member: source and callers.
    assert "maxLines=60" in by_id[PLACE_ORDER]["source"]
    assert by_id[PLACE_ORDER]["callers"]["totalSites"] == 1
    # A type and an added private member: source only.
    assert by_id[ORDER_SERVICE]["callers"] is None
    assert by_id[VALIDATE]["callers"] is None
    assert by_id[VALIDATE]["errors"] == []
    # Failed lookups are reported next to the symbol instead of failing the request.
    assert by_id[BROKEN]["source"] is None
    assert [e.split(":")[0] for e in by_id[BROKEN]["errors"]] == ["source", "callers"]


def test_review_context_respects_max_symbols(client: TestClient) -> None:
    body = client.post("/v1/review-context", json={"baseRef": "main", "maxSymbols": 1}).json()

    assert len(body["changes"]["symbols"]) == 4
    assert [entry["symbol"]["id"] for entry in body["symbols"]] == [ORDER_SERVICE]
    assert body["skippedSymbols"] == 3


def test_openapi_documents_camel_case_fields(client: TestClient) -> None:
    schema = client.get("/openapi.json").json()

    assert "baseRef" in schema["components"]["schemas"]["ChangeRequest"]["properties"]
    assert "/v1/review-context" in schema["paths"]
