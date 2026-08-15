"""Golden dataset loading (data/golden-dataset/cases.json, docs/evaluation.md §2).

The dataset is versioned (top-level ``version``, currently "v1"). Backward
compatibility: the loader tolerates a missing version (reports "unknown") and
unknown extra keys — only ``id``, ``query``, and ``expected_evidence`` are required
per case. ``difficulty``/``archetype``/``notes`` are optional metadata used only
for grouping and skip reporting.
"""

from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable

GOLDEN_DATASET_PATH = Path(__file__).resolve().parents[3] / "data" / "golden-dataset" / "cases.json"


@dataclass(frozen=True)
class GoldenCase:
    id: str
    query: str
    expected_evidence: tuple[str, ...]
    archetype: str | None = None
    difficulty: str | None = None
    notes: str | None = None


@dataclass(frozen=True)
class GoldenDataset:
    version: str
    cases: tuple[GoldenCase, ...]
    source_path: str

    @property
    def count(self) -> int:
        return len(self.cases)


def load_dataset(path: Path | str = GOLDEN_DATASET_PATH) -> GoldenDataset:
    raw = json.loads(Path(path).read_text(encoding="utf-8"))
    version = str(raw.get("version") or raw.get("dataset_version") or "unknown")
    cases_raw = raw.get("cases")
    if not isinstance(cases_raw, list):
        raise ValueError(f"Dataset {path} has no 'cases' list.")

    cases: list[GoldenCase] = []
    for item in cases_raw:
        case_id = str(item.get("id") or "")
        query = str(item.get("query") or "")
        expected = item.get("expected_evidence") or []
        if not case_id or not query.strip():
            continue  # never silently fail the whole run for a malformed entry
        cases.append(
            GoldenCase(
                id=case_id,
                query=query.strip(),
                expected_evidence=tuple(str(e).strip() for e in expected if str(e).strip()),
                archetype=item.get("archetype"),
                difficulty=item.get("difficulty"),
                notes=item.get("notes"),
            )
        )
    return GoldenDataset(version=version, cases=tuple(cases), source_path=str(Path(path).resolve()))


def source_key(path: str | None, title: str | None) -> str | None:
    """Map a retrieved chunk to its gold-annotation key.

    Gold annotations name documents by file (``TokenService.cs``, ``auth-001-jwt-key
    -rotation.md``). Code chunks store the repo-relative path, markdown chunks store
    the file name; matching uses the basename of the path, falling back to the title.
    Returns None for a chunk that cannot be attributed (skipped, never a failure).
    """
    if path:
        name = path.replace("\\", "/").rsplit("/", 1)[-1]
        if name:
            return name
    if title and str(title).strip():
        return str(title).strip()
    return None


def gold_match_key(item_key: str | None, relevant: Iterable[str]) -> bool:
    """Case-sensitive exact match first, then case-insensitive.

    Documented in docs/evaluation.md §3: matching is mechanical and deterministic;
    there is no semantic similarity judgment anywhere in the evaluator.
    """
    if item_key is None:
        return False
    for gold in relevant:
        if item_key == gold:
            return True
    lower = item_key.lower()
    return any(gold.lower() == lower for gold in relevant)
