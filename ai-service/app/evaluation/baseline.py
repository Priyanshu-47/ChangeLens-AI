"""Baseline comparison for regression detection (docs/evaluation.md §8).

A baseline is the previous run's machine-readable report (JSON). Comparison is a
pure function: for each leg and K it reports the delta of Recall@K / MRR / Hit
Rate. Deltas are informational in Phase 7 — no threshold fails the run until a
regression policy is justified by data (CI must never gate on invented numbers).
"""

from __future__ import annotations

import json
from pathlib import Path
from typing import Any


def load_baseline(path: str | Path) -> dict | None:
    """Load a baseline report; returns None when absent or unreadable (never fake)."""
    p = Path(path)
    if not p.exists():
        return None
    try:
        data = json.loads(p.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return None
    return data if isinstance(data, dict) else None


def compare(current: dict, baseline: dict) -> dict:
    """Delta table: baseline → current for each leg/K metric.

    Keys are (leg, K, metric). Values are absolute deltas rounded to 6 places.
    A metric missing from either side is omitted — never reported as 0.
    """
    deltas: dict[str, dict] = {}
    current_legs = (current.get("summary") or {}).get("legs") or {}
    baseline_legs = (baseline.get("summary") or {}).get("legs") or {}

    for leg, cur_leg in current_legs.items():
        base_leg = baseline_legs.get(leg)
        if base_leg is None:
            continue
        leg_deltas: dict[str, dict[str, float]] = {}
        for k, cur_metrics in (cur_leg.get("perK") or {}).items():
            base_metrics = (base_leg.get("perK") or {}).get(k)
            if base_metrics is None:
                continue
            metric_deltas: dict[str, float] = {}
            for metric in ("recall@k", "precision@k", "mrr", "hit_rate"):
                cur_val = cur_metrics.get(metric)
                base_val = base_metrics.get(metric)
                if cur_val is None or base_val is None:
                    continue
                metric_deltas[metric] = round(cur_val - base_val, 6)
            if metric_deltas:
                leg_deltas[str(k)] = metric_deltas
        if leg_deltas:
            deltas[leg] = leg_deltas
    return deltas


def render_deltas(deltas: dict, markdown: bool = False) -> str:
    if not deltas:
        return "No baseline available."
    lines: list[str] = []
    if not markdown:
        lines.append("Baseline comparison (delta = current - baseline):")
        for leg, by_k in sorted(deltas.items()):
            for k, metrics in sorted(by_k.items(), key=lambda kv: int(kv[0])):
                parts = ", ".join(f"{m}={v:+.3f}" for m, v in sorted(metrics.items()))
                lines.append(f"  {leg} @K={k}: {parts}")
    else:
        lines.append("## Baseline comparison (delta = current − baseline)")
        lines.append("")
        lines.append("| Leg | K | Recall@K | MRR | Hit Rate |")
        lines.append("| --- | --- | --- | --- | --- |")
        for leg, by_k in sorted(deltas.items()):
            for k, metrics in sorted(by_k.items(), key=lambda kv: int(kv[0])):
                lines.append(
                    f"| {leg} | {k} | {metrics.get('recall@k', ''):+.3f} | "
                    f"{metrics.get('mrr', ''):+.3f} | {metrics.get('hit_rate', ''):+.3f} |"
                )
    return "\n".join(lines)
