"""Spec 02 §10: embed chunks in batches and cache the vectors per file in ``.cache/index/``."""

from __future__ import annotations

import hashlib
import json
import logging
from collections.abc import Sequence
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

from qalab.llm.base import LLMProvider, normalize_rows
from qalab.rag.chunk import Chunk, chunk_markdown

log = logging.getLogger(__name__)

DEFAULT_INDEX_DIR = Path(".cache") / "index"
BATCH_SIZE = 32


@dataclass
class DocIndex:
    """All chunks of all docs plus one normalized float32 row per chunk (same order)."""

    chunks: list[Chunk] = field(default_factory=list)
    matrix: np.ndarray = field(default_factory=lambda: np.zeros((0, 0), dtype=np.float32))
    model: str = ""
    reembedded_files: list[str] = field(default_factory=list)  # cache misses this build

    def __len__(self) -> int:
        return len(self.chunks)


def cache_key(text: str, model: str) -> str:
    return hashlib.sha256(f"{model}\x00{text}".encode()).hexdigest()


def embed_in_batches(
    provider: LLMProvider, texts: Sequence[str], batch_size: int = BATCH_SIZE
) -> np.ndarray:
    if not texts:
        return np.zeros((0, 0), dtype=np.float32)
    parts = [
        provider.embed(list(texts[start : start + batch_size]))
        for start in range(0, len(texts), batch_size)
    ]
    return normalize_rows(np.vstack(parts))


def build_index(
    doc_paths: Sequence[Path],
    provider: LLMProvider,
    index_dir: Path = DEFAULT_INDEX_DIR,
    chunk_chars: int = 800,
    overlap_chars: int = 100,
    batch_size: int = BATCH_SIZE,
) -> DocIndex:
    """Chunk + embed every file; a file whose contents and model match a cached .npz is reused."""
    model = getattr(provider, "embed_model", provider.model)
    index = DocIndex(model=model)
    matrices: list[np.ndarray] = []
    index_dir.mkdir(parents=True, exist_ok=True)
    for path in doc_paths:
        text = path.read_text(encoding="utf-8")
        key = cache_key(text, model)
        npz_path, meta_path = index_dir / f"{key}.npz", index_dir / f"{key}.json"
        if npz_path.is_file() and meta_path.is_file():
            chunks = [Chunk(**c) for c in json.loads(meta_path.read_text(encoding="utf-8"))]
            matrix = np.load(npz_path)["embeddings"]
            log.debug("index cache hit for %s", path.name)
        else:
            chunks = chunk_markdown(text, path.name, chunk_chars, overlap_chars)
            matrix = embed_in_batches(provider, [c.embed_text for c in chunks], batch_size)
            np.savez(npz_path, embeddings=matrix.astype(np.float32))
            meta_path.write_text(json.dumps([c.to_dict() for c in chunks]), encoding="utf-8")
            index.reembedded_files.append(path.name)
            log.info("embedded %d chunks from %s", len(chunks), path.name)
        index.chunks.extend(chunks)
        if len(chunks):
            matrices.append(matrix)
    if matrices:
        index.matrix = np.vstack(matrices).astype(np.float32)
    return index
