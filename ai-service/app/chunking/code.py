"""Structure-aware code chunking with tree-sitter (ADR-0011).

The AI service owns retrieval-granularity chunking (tree-sitter); deep symbol and
dependency analysis stays in .NET (Roslyn, Phase 4). Chunks preserve hierarchy:
a method chunk knows its file, namespace, and class via metadata; a class chunk
carries the class header plus a member index (not duplicated bodies).

Languages with grammars: csharp, javascript, typescript, python. If a grammar is
missing for a language, the fallback is one honest File-level chunk (never a
blind N-character split).
"""

from __future__ import annotations

import logging

from .base import Chunk, Chunker

logger = logging.getLogger(__name__)

# Grammar package name -> (parser language factory, container node kinds, member node kinds)
_LANGUAGES: dict[str, dict] = {}


def _load_language(
    name: str,
    pkg: str,
    containers: tuple[str, ...],
    members: tuple[str, ...],
    factory_attr: str = "language",
) -> None:
    try:
        import importlib

        module = importlib.import_module(pkg)
        from tree_sitter import Language, Parser

        factory = getattr(module, factory_attr)
        _LANGUAGES[name] = {
            "language": Language(factory()),
            "Parser": Parser,
            "containers": containers,
            "members": members,
        }
    except Exception as exc:  # noqa: BLE001 - optional grammar
        logger.warning("Code chunker: grammar %s unavailable (%s); falling back to file chunks", pkg, exc)


_load_language("csharp", "tree_sitter_c_sharp",
    ("class_declaration", "interface_declaration", "record_declaration", "struct_declaration", "enum_declaration"),
    ("method_declaration", "constructor_declaration", "property_declaration", "destructor_declaration"))
_load_language("javascript", "tree_sitter_javascript",
    ("class_declaration", "function_declaration", "method_definition"),
    ("method_definition", "function_declaration"))
_load_language("typescript", "tree_sitter_typescript",
    ("class_declaration", "interface_declaration", "type_alias_declaration", "enum_declaration", "function_declaration"),
    ("method_definition", "function_declaration"),
    factory_attr="language_typescript")
_load_language("python", "tree_sitter_python",
    ("class_definition", "function_definition"),
    ("function_definition",))

_CHUNK_TYPE_BY_NODE = {
    "class_declaration": "Class",
    "interface_declaration": "Interface",
    "record_declaration": "Record",
    "struct_declaration": "Struct",
    "enum_declaration": "Enum",
    "method_declaration": "Method",
    "constructor_declaration": "Constructor",
    "property_declaration": "Property",
    "destructor_declaration": "Destructor",
    "method_definition": "Method",
    "function_declaration": "Function",
    "function_definition": "Function",
    "class_definition": "Class",
    "type_alias_declaration": "TypeAlias",
}

_SUPPORTED = ("csharp", "javascript", "typescript", "python")


class CodeChunker:
    """Structure-aware chunker for supported languages (grammar-based)."""

    def __init__(self, language: str):
        self.language = language.lower()

    def chunk(self, content: str, *, path: str | None = None) -> list[Chunk]:
        if self.language not in _LANGUAGES:
            # Honest fallback: a single file-level chunk. Never a blind split.
            logger.warning("Code chunker: no grammar for %r; emitting a single file chunk", self.language)
            return [Chunk(chunk_type="File", content=content, path=path)]

        spec = _LANGUAGES[self.language]
        parser = spec["Parser"](spec["language"])
        try:
            tree = parser.parse(content.encode("utf-8"))
        except Exception as exc:  # noqa: BLE001 - malformed input must not break ingestion
            logger.warning("Code chunker: parse failed for %s (%s); emitting file chunk", path, exc)
            return [Chunk(chunk_type="File", content=content, path=path)]

        chunks = _walk(tree.root_node, content, path, spec["containers"], spec["members"], namespace=None, klass=None)
        if not chunks:
            # Error-only parse (malformed/edge-case input): one honest file chunk.
            logger.warning("Code chunker: no structural nodes for %s; emitting file chunk", path)
            return [Chunk(chunk_type="File", content=content, path=path)]
        return chunks


def _walk(node, source: str, path: str | None, containers, members, *, namespace, klass) -> list[Chunk]:
    chunks: list[Chunk] = []
    for child in node.children:
        if child.type == "namespace_declaration" and child.child_by_field_name("name") is not None:
            ns = _node_text(child.child_by_field_name("name"), source)
            chunks.extend(_walk(child, source, path, containers, members, namespace=ns, klass=None))
            continue
        if child.type in containers:
            name = _name(child, source)
            member_chunks: list[Chunk] = []
            # Members may be direct children or nested in a body node
            # (declaration_list / class_body / object — grammar-dependent).
            for member in _member_nodes(child, members):
                member_chunks.append(
                    Chunk(
                        chunk_type=_CHUNK_TYPE_BY_NODE.get(member.type, "Member"),
                        symbol=_name(member, source),
                        content=_node_text(member, source),
                        path=path,
                        metadata={"namespace": namespace, "class": name},
                    )
                )
            if member_chunks:
                # Class-level chunk: header + member index (no duplicated bodies).
                header = _node_text(child, source)
                index = "\n".join(f"// {c.chunk_type}: {c.symbol}" for c in member_chunks)
                chunks.append(
                    Chunk(
                        chunk_type=_CHUNK_TYPE_BY_NODE.get(child.type, "Container"),
                        symbol=name,
                        content=f"{header}\n// members:\n{index}",
                        path=path,
                        metadata={"namespace": namespace, "class": name},
                    )
                )
                chunks.extend(member_chunks)
            else:
                chunks.append(
                    Chunk(
                        chunk_type=_CHUNK_TYPE_BY_NODE.get(child.type, "Container"),
                        symbol=name,
                        content=_node_text(child, source),
                        path=path,
                        metadata={"namespace": namespace, "class": name},
                    )
                )
            continue
        # Recurse into unnamed blocks (e.g. file-scoped namespaces).
        chunks.extend(_walk(child, source, path, containers, members, namespace=namespace, klass=klass))
    return chunks


def _member_nodes(container, members) -> list:
    """Collect member nodes, descending one level into body/declaration wrappers."""
    found: list = []
    for child in container.children:
        if child.type in members:
            found.append(child)
        elif child.type in ("declaration_list", "class_body", "object", "body"):
            found.extend(c for c in child.children if c.type in members)
    return found


def _name(node, source: str) -> str | None:
    field = node.child_by_field_name("name")
    if field is not None:
        return _node_text(field, source)
    # Fallback: first identifier-ish leaf.
    for leaf in _leaves(node):
        if leaf.type in ("identifier", "type_identifier", "class_name"):
            return _node_text(leaf, source)
    return None


def _leaves(node):
    if node.child_count == 0:
        yield node
        return
    for child in node.children:
        yield from _leaves(child)


def _node_text(node, source: str) -> str:
    return source.encode("utf-8")[node.start_byte : node.end_byte].decode("utf-8", errors="replace")


class CodeChunkerFactory:
    @staticmethod
    def for_language(language: str | None) -> Chunker:
        lang = (language or "unknown").lower()
        if lang in _SUPPORTED:
            return CodeChunker(lang)
        # Unknown language → file-level chunk (grammars cover the demo languages).
        return _FileChunker()


class _FileChunker:
    def chunk(self, content: str, *, path: str | None = None) -> list[Chunk]:
        return [Chunk(chunk_type="File", content=content, path=path)]
