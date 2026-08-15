"""HTTP-level tests via TestClient (mock provider — zero Gemini calls)."""

import uuid

TEST_INTERNAL_KEY = "test-internal-key"


def auth_headers(**extra) -> dict[str, str]:
    headers = {
        "X-Internal-Key": TEST_INTERNAL_KEY,
        "X-Contract-Version": "1",
    }
    headers.update(extra)
    return headers


def valid_body(**overrides) -> dict:
    body = {
        "projectId": "p1",
        "changeSummary": "Changed token refresh logic in AuthClient.",
        "changedFiles": [
            {
                "path": "src/AuthClient.cs",
                "changeType": "modified",
                "language": "csharp",
                "symbolsChanged": ["RefreshAsync"],
            }
        ],
    }
    body.update(overrides)
    return body


# --- ops endpoints ---


def test_health_is_open_and_does_not_need_auth(client):
    response = client.get("/health")
    assert response.status_code == 200
    assert response.json()["status"] == "ok"


def test_ready_reports_config_and_mock_provider(client):
    response = client.get("/ready")
    assert response.status_code == 200
    payload = response.json()
    assert payload["status"] == "ready"
    assert payload["checks"]["configuration"] == "ok"
    assert payload["checks"]["provider"] == "mock"
    assert payload["checks"]["readinessProbe"] is False


# --- internal auth ---


def test_internal_health_requires_key(client):
    assert client.get("/internal/v1/health/live").status_code == 401
    assert (
        client.get(
            "/internal/v1/health/live", headers={"X-Internal-Key": "wrong-key", "X-Contract-Version": "1"}
        ).status_code
        == 401
    )
    assert (
        client.get(
            "/internal/v1/health/live", headers={"X-Internal-Key": "test-internal-key"}
        ).status_code
        == 400  # missing contract version
    )


def test_internal_health_ok_with_credentials(client):
    response = client.get("/internal/v1/health/live", headers=auth_headers())
    assert response.status_code == 200
    assert response.json()["checks"]["process"] == "ok"


def test_internal_ready_with_credentials(client):
    response = client.get("/internal/v1/health/ready", headers=auth_headers())
    assert response.status_code == 200
    assert response.json()["status"] == "ready"


# --- analysis endpoint ---


def test_analysis_requires_internal_auth(client):
    response = client.post("/internal/v1/analysis/risk", json=valid_body())
    assert response.status_code == 401


def test_analysis_rejects_invalid_body(client):
    response = client.post(
        "/internal/v1/analysis/risk",
        headers=auth_headers(),
        json=valid_body(changedFiles=[]),
    )
    assert response.status_code == 400
    body = response.json()
    assert body["code"] == "INVALID_REQUEST"
    assert body["details"]["errors"]


def test_analysis_with_mock_provider_returns_validated_result(client):
    response = client.post(
        "/internal/v1/analysis/risk", headers=auth_headers(), json=valid_body()
    )
    assert response.status_code == 200
    payload = response.json()
    assert payload["analysisType"] == "change-risk"
    assert payload["result"]["riskLevel"] in {"LOW", "MEDIUM", "HIGH", "CRITICAL"}
    assert 0.0 <= payload["result"]["confidence"] <= 1.0
    assert payload["usage"]["validationStatus"] == "valid"
    assert payload["usage"]["model"] == "mock-gemini-3.1-flash-lite"
    # Mock is grounded by construction: factor evidence references an input id.
    factor = payload["result"]["riskFactors"][0]
    assert any(
        e["reference"] == "change:src/AuthClient.cs" for e in factor["evidence"]
    )


def test_analysis_error_envelope_never_leaks_stack_traces(client):
    response = client.post(
        "/internal/v1/analysis/risk",
        headers=auth_headers(),
        json=valid_body(changedFiles=[]),
    )
    body = response.json()
    assert "traceback" not in body["detail"].lower()
    assert "TraceId" not in body  # envelope field is camelCase traceId


# --- correlation id ---


def test_correlation_id_echoed(client):
    response = client.get("/health", headers={"X-Correlation-ID": "trace-123"})
    assert response.headers["X-Correlation-ID"] == "trace-123"


def test_correlation_id_generated_when_missing(client):
    response = client.get("/health")
    value = response.headers.get("X-Correlation-ID")
    assert value
    uuid.UUID(value)


def test_correlation_id_in_error_envelope(client):
    response = client.post(
        "/internal/v1/analysis/risk",
        headers=auth_headers(**{"X-Correlation-ID": "trace-abc"}),
        json={},
    )
    assert response.json()["traceId"] == "trace-abc"


# --- incident investigation endpoint (Phase 5) ---


def incident_body(**overrides) -> dict:
    body = {
        "projectId": "p1",
        "analysisId": "a1",
        "promptVersion": "incident-v1",
        "incident": {
            "title": "HTTP 401 after JWT signing-key rotation",
            "severity": "Sev1",
            "status": "Open",
            "environment": "production",
            "service": "acmepay-api",
            "symptoms": ["JwtSecurityTokenHandler: IDX10503 signature validation failed"],
            "knownFacts": ["Severity: Sev1"],
            "unknowns": ["No deployment timestamp was supplied."],
            "timeline": [
                {
                    "occurredAtUtc": "2026-08-01T08:55:00Z",
                    "type": "deployment",
                    "message": "Deployed signing-key rotation",
                }
            ],
        },
    }
    body.update(overrides)
    return body


def test_incident_analysis_requires_internal_auth(client):
    response = client.post("/internal/v1/analysis/incident", json=incident_body())
    assert response.status_code == 401


def test_incident_analysis_rejects_invalid_body(client):
    response = client.post(
        "/internal/v1/analysis/incident",
        headers=auth_headers(),
        json=incident_body()["incident"],  # missing projectId at top level
    )
    assert response.status_code == 400
    assert response.json()["code"] == "INVALID_REQUEST"


def test_incident_analysis_with_mock_returns_grounded_result(client):
    response = client.post(
        "/internal/v1/analysis/incident", headers=auth_headers(), json=incident_body()
    )
    assert response.status_code == 200
    payload = response.json()
    assert payload["analysisType"] == "incident"
    assert payload["usage"]["validationStatus"] == "valid"
    assert payload["usage"]["promptVersion"] == "incident-v1"
    assert payload["result"]["remediation"]["insufficientEvidence"] is True
    assert isinstance(payload["result"]["unknowns"], list)
