import logging
from pathlib import Path

import pytest

from qalab.llm.cache import CachedProvider, LLMCache, cache_key
from qalab.llm.factory import make_provider
from qalab.llm.fake import FakeProvider
from qalab.llm.ollama import OllamaProvider

SCHEMA = {"type": "object"}


def test_cache_hit_skips_the_provider(tmp_path: Path, caplog: pytest.LogCaptureFixture) -> None:
    inner = FakeProvider()
    cache = LLMCache(tmp_path / "llm.sqlite")
    provider = CachedProvider(inner, cache, prompt_version="triage-v1")
    with caplog.at_level(logging.INFO, logger="qalab.llm"):
        first = provider.complete_json("s", "[E1] x", SCHEMA)
        second = provider.complete_json("s", "[E1] x", SCHEMA)
    assert first.cached is False and second.cached is True
    assert second.data == first.data and second.raw == first.raw
    assert len(inner.calls) == 1
    assert cache.hits == 1 and cache.misses == 1
    assert "latency=" in caplog.text and "tokens_in=" in caplog.text
    cache.close()

    reopened = CachedProvider(FakeProvider(), LLMCache(tmp_path / "llm.sqlite"), "triage-v1")
    assert reopened.complete_json("s", "[E1] x", SCHEMA).cached is True


def test_no_cache_passes_through(tmp_path: Path) -> None:
    inner = FakeProvider()
    provider = CachedProvider(inner, None)
    provider.complete_json("s", "u", SCHEMA)
    provider.complete_json("s", "u", SCHEMA)
    assert len(inner.calls) == 2


def test_cache_key_changes_with_every_input() -> None:
    base = dict(
        provider="fake",
        model="m",
        prompt_version="v1",
        system="s",
        user="u",
        schema=SCHEMA,
        images=None,
    )
    reference = cache_key(**base)
    for field, value in [
        ("provider", "ollama"),
        ("model", "m2"),
        ("prompt_version", "v2"),
        ("system", "S"),
        ("user", "U"),
        ("schema", {"type": "array"}),
        ("images", [b"img"]),
    ]:
        assert cache_key(**{**base, field: value}) != reference, field
    assert cache_key(**base) == reference


def test_embed_passes_through(tmp_path: Path) -> None:
    provider = CachedProvider(FakeProvider(), LLMCache(tmp_path / "c.sqlite"))
    assert provider.embed(["a"]).shape == (1, 256)


def test_factory(tmp_path: Path) -> None:
    assert make_provider("none") is None
    fake = make_provider("fake", cache_path=tmp_path / "c.sqlite", prompt_version="triage-v1")
    assert isinstance(fake, CachedProvider) and fake.name == "fake"
    assert fake.prompt_version == "triage-v1"
    ollama = make_provider("ollama", model="x", use_cache=False)
    assert isinstance(ollama, CachedProvider) and isinstance(ollama.inner, OllamaProvider)
    assert ollama.cache is None and ollama.model == "x"
    with pytest.raises(ValueError, match="not built yet"):
        make_provider("openai")
    with pytest.raises(ValueError):
        make_provider("gpt")
