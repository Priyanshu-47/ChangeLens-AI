"""Reciprocal Rank Fusion (docs/rag-architecture.md §5).

Combines per-strategy RANKINGS (not raw scores): each item scores
Σ 1/(RRF_K + rank) across the legs where it appears. Deterministic — ties break
on item id — so evaluation can replay queries and compare strategies (ADR-0010).
"""

from __future__ import annotations


def reciprocal_rank_fusion(
    rankings: list[list[str]], *, k: int = 60
) -> list[tuple[str, float]]:
    """rankings: one ordered list of item ids per retrieval leg (best first)."""
    if k < 1:
        raise ValueError(f"RRF k must be >= 1, got {k}.")
    scores: dict[str, float] = {}
    for ranking in rankings:
        for rank, item_id in enumerate(ranking, start=1):
            scores[item_id] = scores.get(item_id, 0.0) + 1.0 / (k + rank)
    # Deterministic: score desc, then id asc.
    return sorted(scores.items(), key=lambda pair: (-pair[1], pair[0]))
