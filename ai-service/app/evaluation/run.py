"""Evaluation CLI (docs/evaluation.md §4).

Usage (from ai-service/):

    DATABASE_URL="postgresql+psycopg://changelens@127.0.0.1:5433/changelens" \
    ./.venv/Scripts/python -m app.evaluation.run

Deterministic: forces AI_PROVIDER=mock and EMBEDDING_PROVIDER=mock — the normal
evaluation makes zero Gemini calls and needs no API key. Requires the demo corpus
seeded (scripts/seed_demo.py) and the retrieval configuration used by the app.

Outputs (gitignored): data/evaluation-output/evaluation-report.json and .md
"""

from __future__ import annotations

import argparse
import json
import logging
import sys
from pathlib import Path

PROJECT_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(Path(__file__).resolve().parents[2]))

from app.config import Settings  # noqa: E402
from app.db import Database, create_engine_for  # noqa: E402
from app.embeddings import build_embedding_provider  # noqa: E402
from app.evaluation.baseline import compare, load_baseline, render_deltas  # noqa: E402
from app.evaluation.dataset import load_dataset  # noqa: E402
from app.evaluation.runner import EvaluationRunner  # noqa: E402
from app.providers import build_provider  # noqa: E402
from app.retrieval.service import RetrievalService  # noqa: E402
from app.services.analysis_service import AnalysisService  # noqa: E402

logger = logging.getLogger("evaluation")
logging.basicConfig(level=logging.WARNING, format="%(levelname)s %(name)s %(message)s")

DEFAULT_OUT_DIR = PROJECT_ROOT / "data" / "evaluation-output"


def main(argv: list[str] | None = None) -> int:
    # Windows consoles default to cp1252; Unicode report symbols must not crash printing.
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
        sys.stderr.reconfigure(encoding="utf-8")

    parser = argparse.ArgumentParser(description="ChangeLens deterministic evaluation runner")
    parser.add_argument("--project-id", default="demo-project")
    parser.add_argument("--dataset", default=str(PROJECT_ROOT / "data" / "golden-dataset" / "cases.json"))
    parser.add_argument("--out-dir", default=str(DEFAULT_OUT_DIR))
    parser.add_argument("--k", type=int, nargs="*", default=[5, 10])
    parser.add_argument("--legs", nargs="*", default=["vector", "keyword", "dependency", "hybrid"])
    parser.add_argument("--no-ai", action="store_true", help="skip the mock-AI pipeline checks")
    parser.add_argument("--baseline", default=None, help="path to a previous evaluation-report.json")
    args = parser.parse_args(argv)

    # Deterministic by construction: mock providers always, regardless of .env.
    settings = Settings(
        ai_provider="mock",
        embedding_provider="mock",
        ai_auto_retrieve=True,
    )
    db = Database(create_engine_for(settings))
    embedding = build_embedding_provider(settings)
    retrieval = RetrievalService(db=db, embedding=embedding, settings=settings)
    provider = build_provider(settings)
    analysis = AnalysisService(provider=provider, settings=settings, retrieval=retrieval)

    dataset = load_dataset(args.dataset)
    if not dataset.cases:
        logger.error("dataset %s contains no usable cases", args.dataset)
        return 2

    runner = EvaluationRunner(
        retrieval=retrieval,
        analysis=analysis,
        project_id=args.project_id,
        k_values=args.k,
        legs=args.legs,
        dataset_version=dataset.version,
        ai_pipeline=not args.no_ai,
        embedding_model=embedding.model,
        embedding_dimension=embedding.dimension,
        ai_model=getattr(provider, "model", None),
    )

    print(f"Evaluating {len(dataset.cases)} cases (dataset {dataset.version}) …")
    case_results = [runner.evaluate_case(case) for case in dataset.cases]
    report = runner.build_report(dataset.cases, case_results)

    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    json_path = out_dir / "evaluation-report.json"
    md_path = out_dir / "evaluation-report.md"
    json_path.write_text(json.dumps(report, indent=2, default=str), encoding="utf-8")
    md_path.write_text(_render_markdown(report), encoding="utf-8")

    print(f"\nReport written: {json_path}")
    print(f"Report written: {md_path}\n")
    _print_summary(report)

    baseline = load_baseline(args.baseline) if args.baseline else None
    if baseline:
        deltas = compare(report, baseline)
        print("\n" + render_deltas(deltas))
    else:
        print("\nNo baseline available.")
    return 0


def _print_summary(report: dict) -> None:
    summary = report["summary"]
    print(f"Cases: {summary['casesEvaluated']}/{summary['casesTotal']} evaluated, "
          f"{summary['casesFailed']} failed")
    for leg, data in summary["legs"].items():
        per = data["perK"]
        for k, metrics in sorted(per.items(), key=lambda kv: int(kv[0])):
            r = metrics["recall@k"]
            m = metrics["mrr"]
            h = metrics["hit_rate"]
            print(f"  {leg:<10} @K={k:<2} recall={r if r is not None else '—':<8} "
                  f"mrr={m if m is not None else '—':<8} hit={h if h is not None else '—'}")
        if data["skipped"]:
            print(f"  {leg:<10} skipped {data['skipped']} case(s): "
                  f"{', '.join(f'{k}×{v}' for k, v in data['skipReasons'].items())}")
    ai = summary["ai"]
    if ai["pipelineEnabled"]:
        print(f"AI pipeline: schema valid {ai['schemaValid']}/{ai['evaluated']}, "
              f"grounded {ai['grounded']}/{ai['evaluated']}, "
              f"coverage avg {ai['coverageAverage'] if ai['coverageAverage'] is not None else '—'}")


def _render_markdown(report: dict) -> str:
    summary = report["summary"]
    lines = [
        "# ChangeLens Evaluation",
        "",
        f"Run: `{report['evaluationRunId']}`  ",
        f"Dataset: **{report['datasetVersion']}** · {summary['casesTotal']} cases "
        f"({summary['casesEvaluated']} evaluated, {summary['casesFailed']} failed)",
        f"Timestamp: {report['timestamp']}",
        "",
        "## Config",
        "",
        "| Key | Value |",
        "| --- | --- |",
        f"| projectId | {report['config']['projectId']} |",
        f"| K values | {', '.join(str(k) for k in report['config']['kValues'])} |",
        f"| legs | {', '.join(report['config']['legs'])} |",
        f"| AI pipeline | {report['config']['aiPipeline']} |",
        f"| embedding model | {report['config']['embeddingModel'] or '—'} |",
        f"| embedding dimension | {report['config']['embeddingDimension'] or '—'} |",
        f"| AI model | {report['config']['aiModel'] or '—'} |",
        "",
        "## Retrieval",
        "",
        "| Leg | K | Recall@K | Precision@K | MRR | Hit Rate |",
        "| --- | --- | --- | --- | --- | --- |",
    ]
    for leg, data in summary["legs"].items():
        for k, metrics in sorted(data["perK"].items(), key=lambda kv: int(kv[0])):
            def fmt(v):
                return "—" if v is None else f"{v:.3f}"

            lines.append(
                f"| {leg} | {k} | {fmt(metrics['recall@k'])} | {fmt(metrics['precision@k'])} | "
                f"{fmt(metrics['mrr'])} | {fmt(metrics['hit_rate'])} |"
            )
        if data["skipped"]:
            lines.append(
                f"| {leg} — skipped {data['skipped']} case(s): "
                f"{'; '.join(f'{k}×{v}' for k, v in data['skipReasons'].items())} |"
            )

    ai = summary["ai"]
    if ai["pipelineEnabled"]:
        coverage = ai["coverageAverage"]
        coverage_text = "—" if coverage is None else f"{coverage:.3f}"
        lines += [
            "",
            "## AI pipeline (mock provider)",
            "",
            f"- Schema valid: **{ai['schemaValid']}/{ai['evaluated']}**",
            f"- Grounded: **{ai['grounded']}/{ai['evaluated']}**",
            f"- Evidence coverage (gold sources cited): {coverage_text}",
            "",
        ]

    tools = summary["tools"]
    if tools.get("evaluated"):
        lines += [
            "## Tool loop (mock provider, AI-service boundary)",
            "",
            f"- Cases: **{tools['evaluated']}**",
            f"- Proposals: {tools['proposals']} (valid {tools['proposalsValid']}, "
            f"validity {tools['proposalValidity']:.3f})",
            f"- Tool calls: {tools['toolCalls']} · rejected {tools['rejected']} · failed {tools['failed']}",
            f"- Loops completed: **{tools['loopCompleted']}/{tools['evaluated']}**",
            f"- Grounding after tools: **{tools['groundingAfterTools']}/{tools['evaluated']}**",
            f"- Tools used: {', '.join(tools['toolsUsed']) or '—'}",
            "",
            "> Per-case tool trace (toolCallsProposed/Executed/Rejected/Failed, toolCallCount,",
            "> loopCompleted, groundingAfterTools) is recorded in evaluation-report.json §cases.",
            "> Rejected/failed are structural zeros at the AI-service boundary — Python never",
            "> executes tools; .NET integration tests cover authorization and rejection.",
            "",
        ]
    return "\n".join(lines) + "\n"


if __name__ == "__main__":
    raise SystemExit(main())
