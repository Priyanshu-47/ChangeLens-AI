"""Canonical AcmePay demo setup — drives the real running Docker stack via HTTP.

Creates a clean "AcmePay" project with a repository, service, the JWT
signing-key rotation incident (with timeline), seeds the retrieval corpus
(mock embeddings, zero Gemini), runs an async incident investigation, and
runs the change-risk analysis. Prints every ID needed for the UI.
"""
from __future__ import annotations

import json
import os
import subprocess
import sys
import time
import urllib.request
import urllib.error

BASE = os.environ.get("CHANGELENS_BASE", "http://localhost:8080/api/v1")
EMAIL = os.environ.get("DEMO_EMAIL", "engineer@changelens.dev")
PASSWORD = os.environ.get("DEMO_PASSWORD", "EngineerPass!2026")
PROJECT_NAME = os.environ.get("DEMO_PROJECT_NAME", "AcmePay")
SEED_SCRIPT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "ai-service", "scripts", "seed_demo.py"))
VENV_PY = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "ai-service", ".venv", "Scripts", "python.exe"))
DB_URL = os.environ.get("DEMO_DB_URL", "postgresql+psycopg://changelens:changelens_dev_password@localhost:5432/changelens")


def call(method: str, path: str, token: str | None = None, body: dict | None = None, expected: int | None = None):
    url = f"{BASE}{path}"
    headers = {"Content-Type": "application/json", "Accept": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=30) as resp:
            status = resp.status
            payload = resp.read().decode()
    except urllib.error.HTTPError as e:
        status = e.code
        payload = e.read().decode()
    if expected is not None and status != expected:
        print(f"  !! {method} {path} -> {status} (expected {expected}): {payload[:300]}")
        return None
    try:
        return json.loads(payload) if payload else {}
    except json.JSONDecodeError:
        return payload


def login():
    r = call("POST", "/auth/login", body={"email": EMAIL, "password": PASSWORD}, expected=200)
    if not r:
        print("FATAL: login failed"); sys.exit(1)
    print(f"login ok: {r['user']['email']} roles={r['user']['roles']}")
    return r["accessToken"]


def ensure_project(token: str):
    projects = call("GET", "/projects?page=1&pageSize=100", token=token, expected=200)
    for p in projects.get("items", []):
        if p["name"] == PROJECT_NAME:
            print(f"project exists: {p['name']} id={p['id']}")
            return p["id"]
    r = call("POST", "/projects", token=token, body={
        "name": PROJECT_NAME,
        "description": "Synthetic AcmePay payments platform — canonical ChangeLens demo (demo data, not production).",
    }, expected=201)
    pid = r["id"]
    print(f"project created: {PROJECT_NAME} id={pid}")
    return pid


def seed_corpus(project_id: str):
    # Seed inside the ai-service container so it reaches postgres over the compose
    # network (no host-port ambiguity) with deterministic mock embeddings.
    db_url = "postgresql+psycopg://changelens:changelens_dev_password@postgres:5432/changelens"
    cmd = [
        "docker", "exec", "-e", "EMBEDDING_PROVIDER=mock",
        "-e", f"DATABASE_URL={db_url}",
        "changelens-ai", "python", "/app/scripts/seed_demo.py",
        "--project-id", project_id, "--reindex",
        "--repository", "/data/demo-repository",
        "--incidents", "/data/demo-incidents",
        "--runbooks", "/data/demo-runbooks",
    ]
    print(f"seeding corpus (mock embeddings, project {project_id[:8]}...)")
    res = subprocess.run(cmd, capture_output=True, text=True, timeout=900)
    out = (res.stdout or "") + (res.stderr or "")
    for line in out.splitlines():
        if any(k in line.lower() for k in ("documents", "chunk", "error", "traceback", "ingest")):
            print("  seed:", line[:160])
    if res.returncode != 0:
        print("  !! seed exited", res.returncode)
    else:
        print("  seed ok")


def main():
    token = login()
    pid = ensure_project(token)

    # Repository + service
    repo = call("POST", f"/projects/{pid}/repositories", token=token, body={
        "name": "acmepay", "url": "https://github.com/acmepay/acmepay", "defaultBranch": "main", "language": "C#",
    }, expected=201)
    svc = call("POST", f"/projects/{pid}/services", token=token, body={
        "name": "payments-api", "language": "C#", "rootPath": "data/demo-repository/src/AcmePay.Api",
    }, expected=201)
    print(f"repository: {repo['name'] if repo else 'n/a'} | service: {svc['name'] if svc else 'n/a'}")
    service_id = svc["id"] if svc else None

    # The canonical incident: HTTP 401 after JWT signing-key rotation
    inc = call("POST", "/incidents", token=token, body={
        "projectId": pid,
        "title": "HTTP 401 after JWT signing-key rotation",
        "severity": "Sev2",
        "status": "Investigating",
        "classification": "authentication",
        "affectedServiceId": service_id,
        "environment": "production",
        "startedAtUtc": "2026-08-14T10:31:02Z",
        "detectedAtUtc": "2026-08-14T10:32:10Z",
        "summary": (
            "After rotating the JWT signing key, clients started receiving HTTP 401 "
            "Unauthorized from TokenService. TokenService.IssueServiceToken validates "
            "tokens against the new key, but the previous key is no longer accepted. "
            "The auth middleware logs 'signature validation failed' for previously "
            "valid sessions. No deployment of TokenService was involved."
        ),
        "events": [
            {"occurredAtUtc": "2026-08-14T10:31:02Z", "type": "Deployment", "source": "cicd",
             "message": "Signing key rotation applied to secret store"},
            {"occurredAtUtc": "2026-08-14T10:31:18Z", "type": "Metric", "source": "gateway",
             "message": "HTTP 401 rate spiked to 42% of requests"},
            {"occurredAtUtc": "2026-08-14T10:31:35Z", "type": "Error", "source": "TokenService",
             "message": "Signature validation failed: token signed with unknown key id"},
            {"occurredAtUtc": "2026-08-14T10:32:10Z", "type": "Log", "source": "oncall",
             "message": "Incident created: authentication failures after key rotation"},
        ],
    }, expected=201)
    print(f"incident: {inc['title'] if inc else 'n/a'} id={inc['id'] if inc else 'n/a'}")
    incident_id = inc["id"]

    seed_corpus(pid)

    # Async incident investigation
    acc = call("POST", f"/incidents/{incident_id}/investigate", token=token, body={}, expected=202)
    if not acc:
        print("FATAL: investigate did not return 202"); sys.exit(1)
    analysis_id = acc["analysisId"]
    print(f"investigation accepted: {analysis_id} status={acc.get('status')}")

    status = "QUEUED"
    t0 = time.time()
    while time.time() - t0 < 180:
        time.sleep(3)
        a = call("GET", f"/analyses/{analysis_id}", token=token, expected=200)
        if not a:
            continue
        status = a.get("status", "?")
        print(f"  analysis status: {status}")
        if status in ("Succeeded", "Failed"):
            break
    print(f"investigation final: {status} ({time.time() - t0:.0f}s)")
    if status == "Succeeded":
        res = a.get("result") or {}
        rcs = res.get("rootCauseCandidates") or []
        print(f"  rootCauseCandidates: {len(rcs)}")
        for c in rcs:
            print(f"    conf={c.get('confidence')} evidence={len(c.get('evidenceIds') or [])} :: {(c.get('description') or '')[:90]}")
        print(f"  unknowns: {len(res.get('unknowns') or [])}")
    elif status == "Failed":
        print("  failure:", a.get("failureCode"), a.get("failureMessage"))

    # Change risk (Workflow A) for the same JWT change
    cr = call("POST", "/analyses/change-risk", token=token, body={
        "projectId": pid,
        "changeSummary": (
            "Rotate the JWT signing key used by TokenService.IssueServiceToken. "
            "The signing key is read from the secret store at startup and cached; "
            "clients holding tokens signed with the previous key now fail signature "
            "validation with HTTP 401."
        ),
        "changedFiles": [
            {"path": "src/AcmePay.Application/Auth/TokenService.cs", "changeType": "modified", "language": "C#"},
            {"path": "src/AcmePay.Api/Startup.cs", "changeType": "modified", "language": "C#"},
        ],
    }, expected=200)
    if cr:
        r = cr.get("result") or cr
        print(f"change-risk: riskLevel={r.get('riskLevel')} confidence={r.get('confidence')} "
              f"validationStatus={r.get('validationStatus')} promptVersion={cr.get('promptVersion')}")
        for f in (r.get("riskFactors") or [])[:4]:
            print(f"  factor: {(f if isinstance(f, str) else f.get('description', str(f)))[:100]}")
        for sym in (r.get("impactedSymbols") or [])[:5]:
            print(f"  impacted: {sym if isinstance(sym, str) else sym.get('name', str(sym))}")

    print("\n=== DEMO READY ===")
    print(f"projectId:   {pid}")
    print(f"incidentId:  {incident_id}")
    print(f"analysisId:  {analysis_id}")
    print(f"UI:          http://localhost:8080/incidents/{incident_id}")
    print(f"             http://localhost:8080/analyses/{analysis_id}")


if __name__ == "__main__":
    main()
