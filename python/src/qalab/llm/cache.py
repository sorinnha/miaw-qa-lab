"""SQLite cache for structured completions, and the wrapper that applies it to any provider."""

from __future__ import annotations

import hashlib
import json
import logging
import sqlite3
from pathlib import Path
from typing import Any

import numpy as np

from qalab.llm.base import LLMProvider, LLMResult, Timer, log_call

log = logging.getLogger("qalab.llm")

DEFAULT_CACHE_PATH = Path(".cache") / "llm.sqlite"


def cache_key(
    provider: str,
    model: str,
    prompt_version: str,
    system: str,
    user: str,
    schema: dict[str, Any],
    images: list[bytes] | None,
) -> str:
    """sha256 over everything that changes the answer. Image bytes enter through their hashes."""
    digest = hashlib.sha256()
    for part in (provider, model, prompt_version, system, user, json.dumps(schema, sort_keys=True)):
        digest.update(part.encode("utf-8"))
        digest.update(b"\x00")
    for img in images or []:
        digest.update(hashlib.sha256(img).digest())
    return digest.hexdigest()


class LLMCache:
    def __init__(self, path: Path = DEFAULT_CACHE_PATH) -> None:
        self.path = Path(path)
        self.path.parent.mkdir(parents=True, exist_ok=True)
        self._conn = sqlite3.connect(self.path)
        self._conn.execute(
            "CREATE TABLE IF NOT EXISTS completions ("
            "key TEXT PRIMARY KEY, raw TEXT NOT NULL, data TEXT NOT NULL, "
            "tokens_in INTEGER, tokens_out INTEGER, latency_ms REAL, created_at TEXT DEFAULT "
            "CURRENT_TIMESTAMP)"
        )
        self.hits = 0
        self.misses = 0

    def get(self, key: str) -> LLMResult | None:
        row = self._conn.execute(
            "SELECT raw, data, tokens_in, tokens_out, latency_ms FROM completions WHERE key = ?",
            (key,),
        ).fetchone()
        if row is None:
            self.misses += 1
            return None
        self.hits += 1
        raw, data, tokens_in, tokens_out, latency_ms = row
        return LLMResult(json.loads(data), raw, tokens_in, tokens_out, latency_ms, cached=True)

    def put(self, key: str, result: LLMResult) -> None:
        self._conn.execute(
            "INSERT OR REPLACE INTO completions (key, raw, data, tokens_in, tokens_out, latency_ms)"
            " VALUES (?, ?, ?, ?, ?, ?)",
            (
                key,
                result.raw,
                json.dumps(result.data),
                result.tokens_in,
                result.tokens_out,
                result.latency_ms,
            ),
        )
        self._conn.commit()

    def close(self) -> None:
        self._conn.close()


class CachedProvider:
    """Wraps a provider: looks up the cache first, logs latency and tokens for every call.

    ``cache=None`` means ``--no-cache``: calls go straight through but are still logged.
    """

    def __init__(
        self, inner: LLMProvider, cache: LLMCache | None, prompt_version: str = "unversioned"
    ) -> None:
        self.inner = inner
        self.cache = cache
        self.prompt_version = prompt_version
        self.name = inner.name
        self.model = inner.model
        self.embed_model: str = getattr(inner, "embed_model", inner.model)

    def complete_json(
        self,
        system: str,
        user: str,
        schema: dict[str, Any],
        images: list[bytes] | None = None,
        temperature: float = 0.0,
    ) -> LLMResult:
        key = cache_key(self.name, self.model, self.prompt_version, system, user, schema, images)
        if self.cache is not None:
            hit = self.cache.get(key)
            if hit is not None:
                log_call(self.name, self.model, hit)
                return hit
        result = self.inner.complete_json(system, user, schema, images, temperature)
        if self.cache is not None:
            self.cache.put(key, result)
        log_call(self.name, self.model, result)
        return result

    def embed(self, texts: list[str]) -> np.ndarray:
        with Timer() as timer:
            matrix = self.inner.embed(texts)
        log.info("%s/%s embed n=%d latency=%.0fms", self.name, self.model, len(texts), timer.ms)
        return matrix
