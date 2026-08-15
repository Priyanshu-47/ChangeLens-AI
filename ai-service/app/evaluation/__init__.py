"""Deterministic evaluation (docs/evaluation.md).

Everything in this package runs with mock/deterministic providers — zero Gemini
calls. Metrics are defined in metrics.py; the golden dataset is data/golden-dataset/
cases.json; the runner is app.evaluation.run (CLI: python -m app.evaluation.run).
"""
