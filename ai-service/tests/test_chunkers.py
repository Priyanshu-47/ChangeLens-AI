"""Structure-aware chunking (docs/rag-architecture.md §3) — pure unit tests, no DB."""

from app.chunking import get_chunker
from app.chunking.base import content_hash, normalize_content


C_SHARP_SAMPLE = """\
using System;
using System.Net.Http;

namespace AcmePay.Application.Payments
{
    public sealed class ProcessPaymentHandler
    {
        private readonly PaymentDbContext _db;

        public ProcessPaymentHandler(PaymentDbContext db)
        {
            _db = db;
        }

        public async Task<PaymentResult> HandleAsync(ProcessPaymentCommand command, CancellationToken ct)
        {
            var payment = Payment.Create(command.CustomerId, command.MerchantId, command.Amount);
            return new PaymentResult(payment.Id);
        }
    }
}
"""


# --- code chunking (tree-sitter C#) ---


def test_csharp_code_chunking_extracts_structural_units():
    chunks = get_chunker("SourceCode", "csharp").chunk(C_SHARP_SAMPLE, path="ProcessPaymentHandler.cs")
    assert chunks, "expected at least one chunk"

    types = {c.chunk_type for c in chunks}
    assert "Class" in types
    assert "Method" in types
    assert "Constructor" in types


def test_csharp_method_chunk_keeps_context():
    chunks = get_chunker("SourceCode", "csharp").chunk(C_SHARP_SAMPLE, path="ProcessPaymentHandler.cs")
    method = next(c for c in chunks if c.chunk_type == "Method")
    assert "HandleAsync" in method.symbol or "HandleAsync" in method.content
    assert "PaymentResult" in method.content  # signature preserved


def test_csharp_chunk_knows_its_file():
    chunks = get_chunker("SourceCode", "csharp").chunk(C_SHARP_SAMPLE, path="src/Handler.cs")
    assert all(c.path == "src/Handler.cs" for c in chunks)


def test_unknown_language_falls_back_to_whole_file():
    chunks = get_chunker("SourceCode", "vbnet").chunk("Class Foo\nEnd Class", path="Foo.vb")
    assert chunks
    # Fallback keeps the file coherent — no meaningless fragmentation.
    assert len(chunks) == 1
    assert chunks[0].chunk_type == "File"


def test_tiny_code_file_stays_one_chunk():
    chunks = get_chunker("SourceCode", "csharp").chunk("class Tiny { }", path="Tiny.cs")
    assert len(chunks) == 1


# --- incident / runbook chunking ---


def test_incident_chunker_preserves_sections():
    incident = """# INC-001: signing key rotation

> SYNTHETIC EVALUATION DATA

## Symptom

Partners saw 401 invalid_signature responses.

## Root Cause

The previous signing key was not kept in the history list.

## Resolution

Re-added the previous key and restarted.
"""
    chunks = get_chunker("Incident", "markdown").chunk(incident, path="inc-001.md")
    symbols = {c.symbol for c in chunks}
    assert "Symptom" in symbols
    assert "Root Cause" in symbols
    assert "Resolution" in symbols


def test_incident_sections_carry_heading_metadata():
    incident = """# INC-002: test

## Symptom

Everything is broken.
"""
    chunks = get_chunker("Incident", "markdown").chunk(incident)
    symptom = next(c for c in chunks if c.symbol == "Symptom")
    assert symptom.metadata["heading"] == "Symptom"


def test_tiny_incident_stays_one_chunk():
    incident = "# INC-003: short\n\nSmall incident without sections."
    chunks = get_chunker("Incident", "markdown").chunk(incident)
    assert len(chunks) == 1
    assert chunks[0].chunk_type == "Section"


def test_runbook_chunker_keeps_sections():
    runbook = """# Runbook: payment timeout

## Symptoms

- 502 gateway_unavailable

## Diagnosis

Check gateway status page.

## Resolution

Tune the timeout.
"""
    chunks = get_chunker("Runbook", "markdown").chunk(runbook)
    symbols = {c.symbol for c in chunks}
    assert {"Symptoms", "Diagnosis", "Resolution"} <= symbols


# --- content hashing / normalization ---


def test_normalization_is_deterministic():
    a = normalize_content("line1\r\nline2  \n\n")
    b = normalize_content("line1\nline2")
    assert a == b


def test_content_hash_changes_with_content():
    assert content_hash("a") != content_hash("b")


def test_content_hash_ignores_crlf_and_trailing_ws():
    assert content_hash("hello\r\nworld\n") == content_hash("hello\nworld")
