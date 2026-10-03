"""The interface every provider implements, plus the result and error types.

C# comparison: ``typing.Protocol`` is structural typing, like a C# interface that a class
satisfies just by having the right members (no ``: LLMProvider`` needed).
"""

from __future__ import annotations

import json
import logging
import time
from dataclasses import dataclass
from typing import Any, Protocol, runtime_checkable

import numpy as np

log = logging.getLogger("qalab.llm")


@dataclass
class LLMResult:
    data: dict[str, Any]  # parsed JSON object
    raw: str  # the model's text, kept for debugging and the cache
    tokens_in: int
    tokens_out: int
    latency_ms: float
    cached: bool = False


class LLMError(RuntimeError):
    """Transport or server failure (HTTP error, timeout, bad response envelope)."""


class LLMOutputError(LLMError):
    """The model answered, but not with a JSON object. ``raw`` holds what it said."""

    def __init__(self, message: str, raw: str) -> None:
        super().__init__(message)
        self.raw = raw


@runtime_checkable
class LLMProvider(Protocol):
    name: str
    model: str

    def complete_json(
        self,
        system: str,
        user: str,
        schema: dict[str, Any],
        images: list[bytes] | None = None,
        temperature: float = 0.0,
    ) -> LLMResult: ...

    def embed(self, texts: list[str]) -> np.ndarray: ...  # rows L2-normalized, float32


def parse_json_object(raw: str) -> dict[str, Any]:
    """Parse the model's text as one JSON object; tolerate a ```json fence around it."""
    text = raw.strip()
    if text.startswith("```"):
        text = text.strip("`")
        if text.startswith("json"):
            text = text[4:]
    try:
        data = json.loads(text)
    except json.JSONDecodeError as exc:
        raise LLMOutputError(f"model output is not JSON: {exc.msg}", raw) from exc
    if not isinstance(data, dict):
        raise LLMOutputError("model output is JSON but not an object", raw)
    return data


def normalize_rows(matrix: np.ndarray) -> np.ndarray:
    """L2-normalize each row (float32) so cosine similarity becomes a dot product."""
    matrix = np.asarray(matrix, dtype=np.float32)
    if matrix.ndim == 1:
        matrix = matrix.reshape(1, -1)
    norms = np.linalg.norm(matrix, axis=1, keepdims=True)
    norms[norms == 0] = 1.0
    return matrix / norms


def estimate_tokens(text: str) -> int:
    """Rough token count (about 4 characters per token) for providers that don't report one."""
    return max(1, len(text) // 4)


class Timer:
    """``with Timer() as t: ...; t.ms`` — like ``Stopwatch`` in C#."""

    def __enter__(self) -> Timer:
        self._start = time.perf_counter()
        self.ms = 0.0
        return self

    def __exit__(self, *exc: object) -> None:
        self.ms = (time.perf_counter() - self._start) * 1000.0


def log_call(provider: str, model: str, result: LLMResult) -> None:
    log.info(
        "%s/%s latency=%.0fms tokens_in=%d tokens_out=%d cached=%s",
        provider,
        model,
        result.latency_ms,
        result.tokens_in,
        result.tokens_out,
        result.cached,
    )
