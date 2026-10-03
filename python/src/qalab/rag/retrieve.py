"""Spec 02 §10: find the design-doc chunks that explain a cluster.

Two retrievers share one interface: ``EmbeddingRetriever`` (dot product over the npz index, via
``cosine_top_k``) and ``TfidfRetriever`` (scikit-learn, used with ``--provider none``).
"""

from __future__ import annotations

from collections.abc import Sequence
from dataclasses import dataclass
from typing import Protocol

import numpy as np
from sklearn.feature_extraction.text import TfidfVectorizer

from qalab.llm.base import LLMProvider
from qalab.rag.chunk import Chunk
from qalab.rag.index import DocIndex
from qalab.triage.cluster import Cluster

DEFAULT_TOP_K = 3
DEFAULT_MIN_SCORE = 0.25


@dataclass(frozen=True)
class RetrievedChunk:
    """Matches ``qalab.triage.context.RetrievedDoc``."""

    chunk_id: str
    source: str
    heading: str
    text: str
    score: float


class Retriever(Protocol):
    def retrieve(self, query: str) -> list[RetrievedChunk]: ...


def cosine_top_k(query: np.ndarray, matrix: np.ndarray, k: int) -> list[tuple[int, float]]:
    """Return the ``k`` rows of ``matrix`` most similar to ``query``, best first.

    YOU WRITE (Sora). Both ``query`` (shape ``(d,)``) and every row of ``matrix`` (shape
    ``(n, d)``) are already L2-normalized, so cosine similarity is just the dot product:
    ``scores = matrix @ query`` gives one score per row. Then pick the ``k`` highest scores
    (``np.argsort`` sorts ascending; think about how to get the largest ones first) and return
    ``[(row_index, score), ...]`` with plain ``int`` and ``float`` values, best first.
    Edge cases: ``k`` larger than ``n`` returns ``n`` pairs; an empty matrix returns ``[]``.
    No thresholding here: the caller drops scores below ``min_score``.

    C# comparison: this is ``matrix.Select((row, i) => (i, Dot(row, query)))
    .OrderByDescending(p => p.Item2).Take(k)`` with NumPy doing the loops.
    """
    raise NotImplementedError("YOU WRITE")


def build_query(cluster: Cluster) -> str:
    """``"{exception_type} {normalized_message} {top frame} {scene} {detector}"``."""
    frame = cluster.top_frame.qualified if cluster.top_frame else ""
    scene = cluster.scenes[0] if cluster.scenes else ""
    parts = [
        cluster.exception_type,
        cluster.normalized_message,
        frame,
        scene,
        cluster.detector or "",
    ]
    return " ".join(p for p in parts if p).strip()


def _as_retrieved(chunk: Chunk, score: float) -> RetrievedChunk:
    return RetrievedChunk(chunk.chunk_id, chunk.source, chunk.heading, chunk.text, float(score))


class EmbeddingRetriever:
    def __init__(
        self,
        index: DocIndex,
        provider: LLMProvider,
        top_k: int = DEFAULT_TOP_K,
        min_score: float = DEFAULT_MIN_SCORE,
    ) -> None:
        self.index = index
        self.provider = provider
        self.top_k = top_k
        self.min_score = min_score

    def retrieve(self, query: str) -> list[RetrievedChunk]:
        if not len(self.index) or not query.strip():
            return []
        vector = self.provider.embed([query])[0]
        hits = cosine_top_k(vector, self.index.matrix, self.top_k)
        return [
            _as_retrieved(self.index.chunks[i], score)
            for i, score in hits
            if score >= self.min_score
        ]


class TfidfRetriever:
    """Lexical fallback: TF-IDF rows are L2-normalized by scikit-learn, so cosine = dot product."""

    def __init__(
        self,
        chunks: Sequence[Chunk],
        top_k: int = DEFAULT_TOP_K,
        min_score: float = DEFAULT_MIN_SCORE,
    ) -> None:
        self.chunks = list(chunks)
        self.top_k = top_k
        self.min_score = min_score
        self._vectorizer = TfidfVectorizer(sublinear_tf=True)
        if self.chunks:
            self._matrix = self._vectorizer.fit_transform([c.embed_text for c in self.chunks])

    def retrieve(self, query: str) -> list[RetrievedChunk]:
        if not self.chunks or not query.strip():
            return []
        scores = (self._matrix @ self._vectorizer.transform([query]).T).toarray().ravel()
        order = np.argsort(-scores, kind="stable")[: self.top_k]
        return [
            _as_retrieved(self.chunks[int(i)], scores[i])
            for i in order
            if scores[i] >= self.min_score
        ]


def make_retriever(
    index: DocIndex,
    provider: LLMProvider | None,
    top_k: int = DEFAULT_TOP_K,
    min_score: float = DEFAULT_MIN_SCORE,
) -> Retriever:
    """``--provider none`` (or an index without vectors) → TF-IDF; otherwise embeddings."""
    if provider is None or not index.matrix.size:
        return TfidfRetriever(index.chunks, top_k, min_score)
    return EmbeddingRetriever(index, provider, top_k, min_score)
