"""Tunables from ``qalab.toml`` (spec 02 §12) with the spec's defaults built in."""

from __future__ import annotations

import logging
import tomllib
from dataclasses import dataclass, field, fields
from pathlib import Path
from typing import Any

log = logging.getLogger(__name__)


@dataclass
class MergeConfig:
    frame_tfidf_threshold: float = 0.5
    frame_embed_threshold: float = 0.80
    tfidf_only_threshold: float = 0.8


@dataclass
class TriageConfig:
    min_level: str = "warning"
    cell_size_m: float = 4.0
    dbscan_eps_m: float = 3.0
    max_reports: int = 25
    context_chars: int = 10_000
    merge: MergeConfig = field(default_factory=MergeConfig)


@dataclass
class RankConfig:
    p1: float = 15.0
    p2: float = 8.0
    p3: float = 3.0
    crash_multiplier: float = 1.25


@dataclass
class LLMConfig:
    provider: str = "ollama"
    model: str | None = None
    embed_model: str | None = None
    temperature: float = 0.0
    max_retries: int = 2


@dataclass
class RagConfig:
    chunk_chars: int = 800
    overlap_chars: int = 100
    top_k: int = 3
    min_score: float = 0.25
    tfidf_min_score: float = 0.1  # the TF-IDF fallback scores lower than embeddings


@dataclass
class Config:
    triage: TriageConfig = field(default_factory=TriageConfig)
    rank: RankConfig = field(default_factory=RankConfig)
    llm: LLMConfig = field(default_factory=LLMConfig)
    rag: RagConfig = field(default_factory=RagConfig)


def _fill(instance: Any, values: dict[str, Any]) -> None:
    """Copy known keys from a TOML table into a dataclass, recursing into nested tables."""
    known = {f.name: f for f in fields(instance)}
    for key, value in values.items():
        if key not in known:
            log.warning("qalab.toml: unknown key %r ignored", key)
            continue
        current = getattr(instance, key)
        if isinstance(value, dict) and hasattr(current, "__dataclass_fields__"):
            _fill(current, value)
        else:
            setattr(instance, key, value)


def load_config(path: Path | None = None) -> Config:
    """Read ``qalab.toml`` (default: the one in the current directory, if any)."""
    config = Config()
    candidate = path or Path("qalab.toml")
    if candidate.is_file():
        with candidate.open("rb") as f:
            _fill(config, tomllib.load(f))
    elif path is not None:
        raise FileNotFoundError(path)
    return config
