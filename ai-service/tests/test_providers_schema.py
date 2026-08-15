"""Deterministic tests for the Gemini responseSchema normalizer (no API calls).

The live API rejects `$ref`/`$defs` and `enum` (verified against gemini-3.7-flash,
Aug 2026); the normalizer inlines refs and drops API-hostile constructs while Pydantic
keeps doing the real validation.
"""

from __future__ import annotations

from app.models.responses import RiskAnalysisResult
from app.providers.gemini import api_safe_schema


def _walk_values(node):
    """Yield every dict in the schema tree (for construct scans)."""
    if isinstance(node, dict):
        yield node
        for v in node.values():
            yield from _walk_values(v)
    elif isinstance(node, list):
        for v in node:
            yield from _walk_values(v)


def test_normalizer_removes_api_hostile_constructs():
    schema = api_safe_schema(RiskAnalysisResult)

    for node in _walk_values(schema):
        assert "$ref" not in node, "refs must be inlined"
        assert "$defs" not in node, "$defs must be dropped"
        assert "enum" not in node, "enum must be dropped (Pydantic validates)"
        assert "default" not in node, "defaults are generation hints only"
        # No string-valued "title" annotations remain (the RiskFactor FIELD named
        # "title" is a property dict and is deliberately preserved).
        assert not any(k == "title" and isinstance(v, str) for k, v in node.items()), "title annotations dropped"


def test_normalizer_preserves_structure_and_optionality():
    schema = api_safe_schema(RiskAnalysisResult)

    assert schema["type"] == "object"
    assert "riskLevel" in schema["properties"]
    assert "required" in schema and "riskLevel" in schema["required"]

    # Optional fields collapse to their non-null type, keeping constraints.
    risk_factors = schema["properties"]["riskFactors"]
    assert risk_factors["type"] == "array"
    factor = risk_factors["items"]
    assert factor["type"] == "object"
    assert "id" in factor["properties"]  # id: str | None -> str
    assert factor["properties"]["id"]["type"] == "string"

    # A field literally named "title" must survive normalization (it is a property,
    # not the JSON-schema "title" annotation).
    assert factor["properties"]["title"]["type"] == "string"
    assert "title" in factor["required"]

    # Nested evidence arrays keep their inner object shape.
    evidence = factor["properties"]["evidence"]
    assert evidence["type"] == "array"
    assert evidence["items"]["type"] == "object"
    assert "reference" in evidence["items"]["required"]


def test_normalizer_inlines_shared_defs():
    schema = api_safe_schema(RiskAnalysisResult)

    # EvidenceItem (a shared def) must appear inline wherever referenced.
    evidence = schema["properties"]["evidence"]
    assert evidence["type"] == "array"
    item = evidence["items"]
    assert item["type"] == "object"
    assert "type" in item["properties"]  # EvidenceType ref resolved to a string property
    assert item["properties"]["type"]["type"] == "string"
