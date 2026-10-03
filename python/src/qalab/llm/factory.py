"""Build the provider named on the CLI / in ``qalab.toml``."""

from __future__ import annotations

from pathlib import Path

from qalab.llm.base import LLMProvider
from qalab.llm.cache import DEFAULT_CACHE_PATH, CachedProvider, LLMCache
from qalab.llm.fake import FakeProvider
from qalab.llm.ollama import OllamaProvider

HOSTED = ("gemini", "openai", "anthropic")
DEFAULT_OLLAMA_MODEL = "llama3.1:8b"
DEFAULT_OLLAMA_EMBED_MODEL = "nomic-embed-text"


def make_provider(
    name: str,
    model: str | None = None,
    embed_model: str | None = None,
    use_cache: bool = True,
    cache_path: Path = DEFAULT_CACHE_PATH,
    prompt_version: str = "unversioned",
) -> LLMProvider | None:
    """``none`` → None (template reports, TF-IDF retrieval). Everything else is cache-wrapped."""
    name = name.lower()
    if name == "none":
        return None
    if name == "fake":
        inner: LLMProvider = FakeProvider(model=model or "fake-1")
    elif name == "ollama":
        inner = OllamaProvider(
            model=model or DEFAULT_OLLAMA_MODEL,
            embed_model=embed_model or DEFAULT_OLLAMA_EMBED_MODEL,
        )
    elif name in HOSTED:
        raise ValueError(f"provider {name!r} is not built yet (optional extra, see spec 02 §9)")
    else:
        raise ValueError(f"unknown provider {name!r}")
    cache = LLMCache(cache_path) if use_cache else None
    return CachedProvider(inner, cache, prompt_version=prompt_version)
