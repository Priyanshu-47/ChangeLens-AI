"""Retrieval metrics — pure functions, exact formulas documented in docs/evaluation.md.

Terminology
-----------
- ``retrieved``: ranked list of retrieved *source keys* (top-K, best first).
- ``relevant``: set of gold source keys for the case.

Only metrics whose assumptions hold are computed: Precision@K is only meaningful
when the top-K list is fully judged (which it is here — every retrieved chunk is
compared against the gold set), MRR requires a ranked list (always true here).
"""

from __future__ import annotations

from statistics import fmean
from typing import Iterable, Sequence


def recall_at_k(retrieved: Sequence[str], relevant: set[str], k: int) -> float:
    """Relevant gold items retrieved in top-K / total relevant gold items.

    Ranges [0, 1]. 1.0 = every gold source was retrieved within the top K.
    """
    if k <= 0:
        raise ValueError("k must be positive.")
    if not relevant:
        return 0.0  # a case with no gold annotation contributes nothing
    top = retrieved[:k]
    hits = sum(1 for item in top if item in relevant)
    return hits / len(relevant)


def precision_at_k(retrieved: Sequence[str], relevant: set[str], k: int) -> float:
    """Relevant items in top-K / K.

    Ranges [0, 1]. Only reported when the case has gold annotations (otherwise the
    denominator says nothing about the judge's intent).
    """
    if k <= 0:
        raise ValueError("k must be positive.")
    if not relevant:
        return 0.0
    top = retrieved[:k]
    if not top:
        return 0.0
    hits = sum(1 for item in top if item in relevant)
    return hits / k


def mrr(retrieved: Sequence[str], relevant: set[str]) -> float:
    """Reciprocal rank: 1 / (rank of the first relevant result), 0 if none retrieved.

    Ranges [0, 1].
    """
    for rank, item in enumerate(retrieved, start=1):
        if item in relevant:
            return 1.0 / rank
    return 0.0


def hit_rate(retrieved: Sequence[str], relevant: set[str], k: int) -> float:
    """1.0 if at least one gold item appears in the top K, else 0.0.

    Aggregated across cases this is the fraction of cases with at least one hit.
    """
    if not relevant:
        return 0.0
    return 1.0 if any(item in relevant for item in retrieved[:k]) else 0.0


def average(values: Iterable[float]) -> float | None:
    """Mean over a case list; None when there are no values (never fabricate 0)."""
    items = list(values)
    if not items:
        return None
    return round(fmean(items), 6)


def fraction(numerator: int, denominator: int) -> float | None:
    """numerator/denominator or None when the denominator is zero."""
    if denominator <= 0:
        return None
    return round(numerator / denominator, 6)


def leg_contribution(hybrid_items: Sequence[dict], leg: str) -> float | None:
    """Fraction of hybrid top-K items whose ``sources`` shows the leg contributed.

    This is *attribution*, not quality: it answers "how often did this leg surface a
    chunk that reached the fused result". Vector attribution uses a present score,
    keyword/dependency attribution uses a present rank.
    """
    if not hybrid_items:
        return None
    key = "keyword_rank" if leg == "keyword" else "dependency_rank" if leg == "dependency" else "vector_score"
    contributed = sum(1 for item in hybrid_items if item.get(key) is not None)
    return round(contributed / len(hybrid_items), 6)
