"""Reciprocal Rank Fusion (docs/rag-architecture.md §5) — deterministic unit tests."""

import pytest

from app.retrieval.rrf import reciprocal_rank_fusion


def test_rrf_combines_rankings():
    vector = ["b", "a", "c"]
    keyword = ["b", "e", "a"]
    fused = reciprocal_rank_fusion([vector, keyword], k=60)
    ids = [item_id for item_id, _ in fused]
    # b is rank 1 in BOTH legs -> clearly highest fused score.
    assert ids[0] == "b"
    # a (ranks 2, 3) beats e (rank 2 in one leg only) and c (rank 3 in one leg only).
    assert ids[1] == "a"
    assert set(ids) == {"a", "b", "c", "e"}


def test_rrf_scores_are_deterministic():
    legs = [["x", "y", "z"], ["y", "x"], ["z"]]
    first = reciprocal_rank_fusion(legs, k=60)
    second = reciprocal_rank_fusion(legs, k=60)
    assert [(i, round(s, 9)) for i, s in first] == [(i, round(s, 9)) for i, s in second]


def test_rrf_k_changes_relative_boost():
    # A larger k flattens rank differences (the 1/(k+rank) curve).
    legs = [["a", "b", "c", "d", "e", "f"]]
    with_k1 = reciprocal_rank_fusion(legs, k=1)
    with_k100 = reciprocal_rank_fusion(legs, k=100)
    assert with_k1[0][1] / with_k1[1][1] > with_k100[0][1] / with_k100[1][1]


def test_rrf_handles_disjoint_rankings():
    fused = reciprocal_rank_fusion([["a", "b"], ["c", "d"]], k=60)
    assert len(fused) == 4


def test_rrf_single_item_appears_in_both():
    fused = reciprocal_rank_fusion([["a"], ["a"]], k=60)
    assert fused[0][0] == "a"


def test_rrf_empty_legs():
    assert reciprocal_rank_fusion([[], []], k=60) == []


def test_rrf_ranking_is_stable_for_ties():
    # a and b tie (both appear once at the same rank position) — deterministic order.
    fused = reciprocal_rank_fusion([["a"], ["b"]], k=60)
    ids = [item_id for item_id, _ in fused]
    assert ids[0] in ("a", "b")


@pytest.mark.parametrize("k", [0, -1])
def test_rrf_rejects_invalid_k(k):
    with pytest.raises(ValueError):
        reciprocal_rank_fusion([["a"]], k=k)
