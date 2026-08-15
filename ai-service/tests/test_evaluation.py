"""Evaluation framework tests (Phase 7): metrics, dataset loading, runner, ablation,
AI-pipeline coverage, retrieval trace, baseline comparison.

Zero Gemini calls: retrieval is a fake, the AI pipeline uses MockAIProvider.
"""

from __future__ import annotations

import pytest

from app.evaluation import baseline
from app.evaluation.dataset import GOLDEN_DATASET_PATH, gold_match_key, load_dataset, source_key
from app.evaluation.metrics import (
    average,
    fraction,
    hit_rate,
    leg_contribution,
    mrr,
    precision_at_k,
    recall_at_k,
)
from app.evaluation.runner import EvaluationRunner, _symbol_like_terms
from app.models.responses import (
    RetrievalResultItem,
    RetrievalResultSources,
    RetrievalSearchResponse,
    RetrievalUsage,
)
from app.providers.mock import MockAIProvider
from app.services.analysis_service import AnalysisService


def make_settings(**overrides):
    from app.config import Settings

    base = {"internal_api_key": "test-internal-key", "ai_provider": "mock"}
    base.update(overrides)
    return Settings(**base)


# --- metrics (exact formulas, docs/evaluation.md §6) -----------------------


def test_recall_at_k_counts_gold_items_in_top_k():
    retrieved = ["A", "B", "C", "D"]
    relevant = {"B", "D", "E"}
    assert recall_at_k(retrieved, relevant, 4) == 2 / 3
    assert recall_at_k(retrieved, relevant, 2) == 1 / 3
    assert recall_at_k(retrieved, relevant, 1) == 0.0


def test_precision_at_k_uses_k_denominator():
    retrieved = ["A", "B", "C", "D"]
    relevant = {"B", "C"}
    assert precision_at_k(retrieved, relevant, 4) == 0.5
    assert precision_at_k(retrieved, relevant, 2) == 0.5


def test_mrr_reciprocal_rank():
    assert mrr(["A", "B", "C"], {"B"}) == 0.5
    assert mrr(["A", "B", "C"], {"A"}) == 1.0
    assert mrr(["A", "B", "C"], {"X"}) == 0.0


def test_hit_rate_and_aggregates():
    assert hit_rate(["A", "B"], {"B"}, 5) == 1.0
    assert hit_rate(["A", "B"], {"X"}, 5) == 0.0
    assert average([1.0, 2.0]) == 1.5
    assert average([]) is None
    assert fraction(3, 4) == 0.75
    assert fraction(3, 0) is None


def test_leg_contribution_attributes_hybrid_items():
    items = [
        {"vector_score": 0.9, "keyword_rank": 1, "dependency_rank": None},
        {"vector_score": None, "keyword_rank": 2, "dependency_rank": 1},
    ]
    assert leg_contribution(items, "vector") == 0.5
    assert leg_contribution(items, "keyword") == 1.0
    assert leg_contribution(items, "dependency") == 0.5
    assert leg_contribution([], "vector") is None


# --- dataset loading -------------------------------------------------------


def test_dataset_loads_version_and_cases():
    dataset = load_dataset()
    assert dataset.version == "v1"
    assert dataset.count == 20
    assert all(c.expected_evidence for c in dataset.cases)
    archetypes = {c.archetype for c in dataset.cases}
    assert "authentication" in archetypes


def test_source_key_normalization():
    assert source_key("src/AcmePay.Application/Auth/TokenService.cs", None) == "TokenService.cs"
    assert source_key("authentication-failure.md", "Authentication Failure") == "authentication-failure.md"
    assert source_key(None, "Runbook title") == "Runbook title"
    assert source_key(None, None) is None


def test_gold_match_exact_then_case_insensitive():
    assert gold_match_key("TokenService.cs", ["TokenService.cs"])
    assert gold_match_key("tokenservice.cs", ["TokenService.cs"])
    assert not gold_match_key("Other.cs", ["TokenService.cs"])


def test_symbol_like_terms_from_query():
    assert "ApiKeyAuthMiddleware" in _symbol_like_terms(["ApiKeyAuthMiddleware path matching bypass"])
    assert _symbol_like_terms(["401 after a key rotation"]) == []  # no CamelCase identifiers


# --- runner with a fake retrieval -----------------------------------------

GOLD = ["TokenService.cs", "auth-001-jwt-key-rotation.md"]


def make_item(chunk_id, path, *, doc_type="SourceCode", vector=None, keyword=None, dependency=None):
    return RetrievalResultItem(
        chunk_id=chunk_id,
        document_id=f"doc-{chunk_id}",
        document_type=doc_type,
        chunk_type="Class",
        source=path,
        content="content",
        metadata={"title": path, "path": path},
        score=0.9,
        sources=RetrievalResultSources(vector=vector, keyword=keyword, dependency=dependency),
    )


class FakeRetrieval:
    """Scripted per-strategy results; search_queries returns the hybrid list."""

    def __init__(self, by_strategy: dict[str, list[RetrievalResultItem]]):
        self.by_strategy = by_strategy

    def search(self, request):
        items = self.by_strategy.get(request.strategy, [])
        return RetrievalSearchResponse(
            results=items[: request.k], usage=RetrievalUsage(strategy=request.strategy)
        )

    def search_queries(self, project_id, queries, dependency=None, k=None):
        return self.by_strategy.get("hybrid", [])[:k]


def simple_case(**overrides):
    data = {
        "id": "case-x",
        "query": "JWT signing key rotation broke authentication.",
        "expected_evidence": GOLD,
        "archetype": "authentication",
        "difficulty": "easy",
    }
    data.update(overrides)
    from app.evaluation.dataset import GoldenCase

    return GoldenCase(**data)


@pytest.fixture
def fake_retrieval():
    token_item = make_item("c1", "src/Auth/TokenService.cs", vector=0.9, keyword=1)
    runbook_item = make_item("c2", "auth-001-jwt-key-rotation.md", vector=0.85, keyword=2, dependency=1)
    other_item = make_item("c3", "src/Payments/RefundsController.cs", vector=0.7, keyword=3)
    return FakeRetrieval(
        {
            "vector": [token_item, other_item],
            "keyword": [other_item, token_item],
            "dependency": [runbook_item, other_item],
            "hybrid": [token_item, runbook_item, other_item],
        }
    )


def test_runner_computes_per_leg_metrics(fake_retrieval):
    runner = EvaluationRunner(
        retrieval=fake_retrieval,
        project_id="p1",
        k_values=[5],
        legs=["vector", "keyword", "hybrid"],
        dataset_version="v1",
        ai_pipeline=False,
    )
    result = runner.evaluate_case(simple_case())

    vector = result.legs["vector"]
    assert vector.status == "evaluated"
    assert vector.scores[0].recall == 1 / 2  # only TokenService.cs retrieved
    assert vector.scores[0].mrr == 1.0
    keyword = result.legs["keyword"]
    assert keyword.scores[0].mrr == 0.5  # first relevant at rank 2
    hybrid = result.legs["hybrid"]
    assert hybrid.scores[0].recall == 1.0
    assert hybrid.scores[0].mrr == 1.0


def test_runner_skips_dependency_leg_without_terms(fake_retrieval):
    runner = EvaluationRunner(
        retrieval=fake_retrieval, project_id="p1", k_values=[5],
        legs=["dependency"], dataset_version="v1", ai_pipeline=False,
    )
    result = runner.evaluate_case(simple_case(query="401 after a key rotation"))
    dep = result.legs["dependency"]
    assert dep.status == "skipped"
    assert "no dependency terms" in dep.skipped_reason


def test_runner_evaluates_dependency_leg_with_identifier_query(fake_retrieval):
    runner = EvaluationRunner(
        retrieval=fake_retrieval, project_id="p1", k_values=[5],
        legs=["dependency"], dataset_version="v1", ai_pipeline=False,
    )
    result = runner.evaluate_case(simple_case(query="ApiKeyAuthMiddleware casing bypass"))
    dep = result.legs["dependency"]
    assert dep.status == "evaluated"
    assert dep.scores[0].recall == 1 / 2  # runbook chunk matches symbol? no — dependency returns runbook+other
    assert dep.retrieved_keys[0] == "auth-001-jwt-key-rotation.md"


def test_runner_dedupes_chunks_of_the_same_gold_document(fake_retrieval):
    """Several chunks of one gold document count as a single hit (document-level
    relevance); recall can never exceed 1.0."""
    dup = FakeRetrieval(
        {
            "hybrid": [
                make_item("c1", "src/Auth/TokenService.cs", keyword=1),
                make_item("c9", "src/Auth/TokenService.cs", keyword=2),  # same document
                make_item("c2", "auth-001-jwt-key-rotation.md", keyword=3),
            ]
        }
    )
    runner = EvaluationRunner(
        retrieval=dup, project_id="p1", k_values=[5],
        legs=["hybrid"], dataset_version="v1", ai_pipeline=False,
    )
    result = runner.evaluate_case(simple_case())
    hybrid = result.legs["hybrid"]
    assert hybrid.scores[0].recall == 1.0  # TokenService.cs + auth-001 = 2/2, not 3/2
    assert hybrid.retrieved_keys == ["TokenService.cs", "auth-001-jwt-key-rotation.md"]
    assert len(hybrid.candidates) == 3  # candidates still list every chunk


def test_runner_report_shape_and_summary(fake_retrieval):
    runner = EvaluationRunner(
        retrieval=fake_retrieval, project_id="p1", k_values=[5, 10],
        legs=["vector", "hybrid"], dataset_version="v1", ai_pipeline=False,
    )
    report = runner.build_report([simple_case()], [runner.evaluate_case(simple_case())])

    assert report["datasetVersion"] == "v1"
    assert report["evaluationRunId"]
    assert report["config"]["kValues"] == [5, 10]
    assert report["summary"]["casesTotal"] == 1
    assert report["summary"]["casesEvaluated"] == 1
    assert "vector" in report["summary"]["legs"]
    assert "hybrid" in report["summary"]["legs"]
    assert report["summary"]["legs"]["hybrid"]["perK"]["5"]["recall@k"] == 1.0
    assert report["cases"][0]["case_id"] == "case-x"
    assert report["cases"][0]["legs"]["hybrid"]["candidates"][0]["keywordRank"] == 1


# --- AI pipeline (mock) + retrieval trace ---------------------------------


def test_ai_pipeline_records_schema_grounding_and_coverage(fake_retrieval):
    settings = make_settings(ai_auto_retrieve=True)
    analysis = AnalysisService(
        provider=MockAIProvider(), settings=settings, retrieval=fake_retrieval
    )
    runner = EvaluationRunner(
        retrieval=fake_retrieval, analysis=analysis, project_id="p1",
        k_values=[5], legs=["hybrid"], dataset_version="v1", ai_pipeline=True,
    )
    result = runner.evaluate_case(simple_case())
    assert result.ai.status == "evaluated"
    assert result.ai.validation_status in ("valid", "repaired")
    assert result.ai.grounded is True
    # MockAIProvider cites the top chunk (TokenService.cs) -> 1 of 2 gold covered.
    assert result.ai.coverage == 0.5
    assert "TokenService.cs" in result.ai.gold_covered


def test_analysis_response_includes_retrieval_trace(fake_retrieval):
    settings = make_settings(ai_auto_retrieve=True)
    analysis = AnalysisService(
        provider=MockAIProvider(), settings=settings, retrieval=fake_retrieval
    )
    import asyncio

    from app.models.requests import RiskAnalysisRequest

    response = asyncio.run(
        analysis.analyze_change_risk(
            RiskAnalysisRequest(
                project_id="p1",
                change_summary="JWT signing key rotation broke authentication.",
                changed_files=[
                    {"path": "src/Auth/TokenService.cs", "change_type": "modified", "language": "csharp"}
                ],
            )
        )
    )
    assert response.trace is not None
    assert response.trace.queries
    assert response.trace.selected_count <= response.trace.candidate_count
    assert response.trace.max_chunks > 0
    item = response.trace.items[0]
    assert item.id.startswith("chunk:")
    assert item.keyword_rank is not None or item.vector_score is not None
    assert item.path is not None


def test_incident_response_includes_retrieval_trace(fake_retrieval):
    settings = make_settings(ai_auto_retrieve=True)
    analysis = AnalysisService(
        provider=MockAIProvider(), settings=settings, retrieval=fake_retrieval
    )
    import asyncio

    from app.models.requests import IncidentAnalysisRequest, IncidentContextItem

    response = asyncio.run(
        analysis.analyze_incident(
            IncidentAnalysisRequest(
                project_id="p1",
                incident=IncidentContextItem(
                    title="401 after JWT signing-key rotation",
                    severity="Sev1",
                    status="Open",
                    symptoms=["IDX10503 signature validation failed"],
                ),
            )
        )
    )
    assert response.trace is not None
    assert response.trace.items
    # Queries = [title] + symptoms + service, with exact technical terms preserved.
    assert response.trace.queries[0] == "401 after JWT signing-key rotation"
    assert any("IDX10503" in q for q in response.trace.queries)


def test_trace_absent_when_retrieval_disabled():
    settings = make_settings(ai_auto_retrieve=False)
    analysis = AnalysisService(provider=MockAIProvider(), settings=settings, retrieval=None)
    import asyncio

    from app.models.requests import IncidentAnalysisRequest, IncidentContextItem

    response = asyncio.run(
        analysis.analyze_incident(
            IncidentAnalysisRequest(
                project_id="p1",
                incident=IncidentContextItem(
                    title="t", severity="Sev1", status="Open",
                    retrieved_documents=[],
                ),
            )
        )
    )
    assert response.trace is None


# --- baseline comparison ---------------------------------------------------


def test_baseline_missing_returns_none(tmp_path):
    assert baseline.load_baseline(tmp_path / "missing.json") is None


def test_baseline_compare_reports_deltas():
    current = {
        "summary": {
            "legs": {
                "hybrid": {"perK": {"5": {"recall@k": 0.8, "mrr": 0.9, "hit_rate": 1.0}}}
            }
        }
    }
    previous = {
        "summary": {
            "legs": {
                "hybrid": {"perK": {"5": {"recall@k": 0.7, "mrr": 0.8, "hit_rate": 1.0}}}
            }
        }
    }
    deltas = baseline.compare(current, previous)
    assert deltas["hybrid"]["5"]["recall@k"] == pytest.approx(0.1)
    assert deltas["hybrid"]["5"]["mrr"] == pytest.approx(0.1)
    assert deltas["hybrid"]["5"]["hit_rate"] == pytest.approx(0.0)


def test_baseline_render_without_baseline():
    assert "No baseline available." in baseline.render_deltas({})
