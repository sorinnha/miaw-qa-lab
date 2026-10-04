"""Gemini provider against a fake SDK client: request shapes, parsing, errors. No network."""

import json
import logging
from pathlib import Path
from types import SimpleNamespace
from typing import Any

import numpy as np
import pytest

from qalab.llm import LLMError, LLMOutputError, LLMProvider
from qalab.llm.cache import CachedProvider
from qalab.llm.factory import make_provider
from qalab.llm.fake import FakeProvider
from qalab.llm.gemini import (
    DEFAULT_EMBED_MODEL,
    DEFAULT_MODEL,
    GeminiProvider,
    build_config,
    build_contents,
    make_client,
    response_schema,
)
from qalab.models.schemas import load_schema

PNG = b"\x89PNG\r\n\x1a\n" + b"\x00" * 8
JPEG = b"\xff\xd8\xff\xe0" + b"\x00" * 8
SECRET = "test-key-do-not-log"


class FakeModels:
    """Stands in for ``client.models``: records calls and returns canned SDK-shaped responses."""

    def __init__(self, text: str | None = '{"ok": true}', error: Exception | None = None) -> None:
        self.text = text
        self.error = error
        self.calls: list[tuple[str, dict[str, Any]]] = []

    def generate_content(self, **kwargs: Any) -> Any:
        self.calls.append(("generate_content", kwargs))
        if self.error:
            raise self.error
        usage = SimpleNamespace(
            prompt_token_count=321, candidates_token_count=40, thoughts_token_count=12
        )
        candidate = SimpleNamespace(finish_reason="SAFETY" if self.text is None else "STOP")
        return SimpleNamespace(text=self.text, usage_metadata=usage, candidates=[candidate])

    def embed_content(self, **kwargs: Any) -> Any:
        self.calls.append(("embed_content", kwargs))
        if self.error:
            raise self.error
        vectors = [[3.0, 4.0], [0.0, 2.0], [1.0, 0.0]][: len(kwargs["contents"])]
        return SimpleNamespace(embeddings=[SimpleNamespace(values=v) for v in vectors])


def _provider(models: FakeModels, **kwargs: Any) -> GeminiProvider:
    return GeminiProvider(client=SimpleNamespace(models=models), **kwargs)


class APIError(Exception):
    """Shaped like google.genai.errors.APIError (has .code); its text includes the prompt."""

    def __init__(self, code: int, message: str) -> None:
        super().__init__(message)
        self.code = code


def test_satisfies_the_protocol_and_defaults() -> None:
    provider = _provider(FakeModels())
    assert isinstance(provider, LLMProvider)
    assert provider.name == "gemini"
    assert (provider.model, provider.embed_model) == (DEFAULT_MODEL, DEFAULT_EMBED_MODEL)


def test_complete_json_sends_schema_temperature_system_and_images() -> None:
    models = FakeModels()
    schema = load_schema("llm_bug_draft")
    result = _provider(models, model="gemini-test").complete_json(
        "SYSTEM RULES", "USER PROMPT", schema, images=[PNG, JPEG]
    )
    assert result.data == {"ok": True} and result.raw == '{"ok": true}'
    assert (result.tokens_in, result.tokens_out) == (321, 52)  # answer 40 + thinking 12
    assert result.cached is False and result.latency_ms >= 0

    method, kwargs = models.calls[0]
    assert method == "generate_content" and kwargs["model"] == "gemini-test"
    config = kwargs["config"]
    assert config["system_instruction"] == "SYSTEM RULES"
    assert config["temperature"] == 0.0
    assert config["response_mime_type"] == "application/json"
    sent_schema = config["response_json_schema"]
    assert "$schema" not in sent_schema and "$id" not in sent_schema and "title" not in sent_schema
    assert sent_schema["properties"] == schema["properties"]
    assert sent_schema["required"] == schema["required"]
    (content,) = kwargs["contents"]
    assert content["role"] == "user"
    assert content["parts"][0] == {"text": "USER PROMPT"}
    assert content["parts"][1] == {"inline_data": {"mime_type": "image/png", "data": PNG}}
    assert content["parts"][2]["inline_data"]["mime_type"] == "image/jpeg"


def test_temperature_is_passed_through() -> None:
    models = FakeModels()
    _provider(models).complete_json("s", "u", {"type": "object"}, temperature=0.4)
    assert models.calls[0][1]["config"]["temperature"] == 0.4
    assert build_contents("u", None) == [{"role": "user", "parts": [{"text": "u"}]}]


@pytest.mark.parametrize(
    ("text", "match"),
    [(None, "no text \\(finish_reason=SAFETY\\)"), ("   ", "no text"), ("not json", "not JSON")],
)
def test_bad_answers_are_output_errors_so_the_report_layer_retries(
    text: str | None, match: str
) -> None:
    with pytest.raises(LLMOutputError, match=match):
        _provider(FakeModels(text=text)).complete_json("s", "u", {"type": "object"})


def test_sdk_errors_become_llm_errors_without_prompt_or_key() -> None:
    error = APIError(429, f"quota exceeded for prompt 'SECRET PROMPT' key={SECRET}")
    with pytest.raises(LLMError) as exc:
        _provider(FakeModels(error=error)).complete_json("s", "SECRET PROMPT", {"type": "object"})
    assert str(exc.value) == "gemini generate_content failed: HTTP 429"
    with pytest.raises(LLMError, match="embed_content failed: ConnectionError"):
        _provider(FakeModels(error=ConnectionError("down"))).embed(["a"])


def test_embed_normalizes_rows_and_checks_counts() -> None:
    models = FakeModels()
    matrix = _provider(models, embed_model="embed-test").embed(["a", "b"])
    assert matrix.dtype == np.float32
    np.testing.assert_allclose(matrix, [[0.6, 0.8], [0.0, 1.0]])
    assert models.calls[0] == ("embed_content", {"model": "embed-test", "contents": ["a", "b"]})
    assert _provider(models).embed([]).shape == (0, 0)

    class ShortModels(FakeModels):
        def embed_content(self, **kwargs: Any) -> Any:
            return SimpleNamespace(embeddings=[SimpleNamespace(values=[1.0])])

    with pytest.raises(LLMError, match="wrong number"):
        _provider(ShortModels()).embed(["a", "b"])


def test_nothing_secret_is_logged(caplog: pytest.LogCaptureFixture, tmp_path: Path) -> None:
    provider = CachedProvider(_provider(FakeModels()), None, "triage-v1")
    with caplog.at_level(logging.DEBUG):
        provider.complete_json("SYSTEM SECRET", "PROMPT SECRET", {"type": "object"})
        provider.embed(["EMBED SECRET"])
    assert "SECRET" not in caplog.text
    assert "latency=" in caplog.text  # latency and tokens are logged


def test_missing_key_is_a_clear_value_error() -> None:
    with pytest.raises(ValueError, match="GEMINI_API_KEY is not set"):
        make_client()
    with pytest.raises(ValueError, match="GEMINI_API_KEY is not set"):
        make_provider("gemini", use_cache=False)


def test_client_gets_the_key_from_the_environment(monkeypatch: pytest.MonkeyPatch) -> None:
    genai = pytest.importorskip("google.genai")
    seen: dict[str, Any] = {}

    class RecordingClient:
        def __init__(self, **kwargs: Any) -> None:
            seen.update(kwargs)
            self.models = FakeModels()

    monkeypatch.setattr(genai, "Client", RecordingClient)
    monkeypatch.setenv("GEMINI_API_KEY", SECRET)
    provider = make_provider("gemini", model="gemini-x", use_cache=False)
    assert seen == {"api_key": SECRET}
    assert isinstance(provider, CachedProvider) and isinstance(provider.inner, GeminiProvider)
    assert (provider.model, provider.embed_model) == ("gemini-x", DEFAULT_EMBED_MODEL)
    assert SECRET not in repr(vars(provider.inner))  # the key is not kept on the provider


def test_requests_match_the_real_sdk_types() -> None:
    """The dict requests validate against google-genai's own pydantic types."""
    types = pytest.importorskip("google.genai.types")
    config = types.GenerateContentConfig.model_validate(
        build_config("rules", load_schema("llm_bug_draft"), 0.0)
    )
    assert config.temperature == 0.0 and config.response_mime_type == "application/json"
    assert config.response_json_schema == response_schema(load_schema("llm_bug_draft"))
    content = types.Content.model_validate(build_contents("prompt", [PNG])[0])
    assert content.parts[0].text == "prompt"
    assert content.parts[1].inline_data.mime_type == "image/png"
    assert content.parts[1].inline_data.data == PNG


class DraftModels(FakeModels):
    """Answers like a well-behaved model: a grounded draft built from the prompt (FakeProvider)."""

    def generate_content(self, **kwargs: Any) -> Any:
        self.calls.append(("generate_content", kwargs))
        prompt = kwargs["contents"][0]["parts"][0]["text"]
        draft = FakeProvider().build_draft(prompt)
        usage = SimpleNamespace(prompt_token_count=100, candidates_token_count=50)
        return SimpleNamespace(text=json.dumps(draft), usage_metadata=usage, candidates=[])

    def embed_content(self, **kwargs: Any) -> Any:
        self.calls.append(("embed_content", kwargs))
        rows = FakeProvider().embed(list(kwargs["contents"]))
        return SimpleNamespace(embeddings=[SimpleNamespace(values=list(r)) for r in rows])


@pytest.mark.youwrite
def test_triage_run_with_gemini_end_to_end(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """`qalab triage run --provider gemini` with the SDK client mocked (needs YOU WRITE code)."""
    from typer.testing import CliRunner

    from qalab.cli import app
    from qalab.models.schemas import first_error

    repo = Path(__file__).resolve().parents[3]
    models = DraftModels()
    monkeypatch.setattr("qalab.llm.gemini.make_client", lambda: SimpleNamespace(models=models))
    monkeypatch.chdir(tmp_path)  # the LLM cache goes to tmp_path/.cache
    out = tmp_path / "report"
    result = CliRunner().invoke(
        app,
        [
            "triage",
            "run",
            str(repo / "samples" / "sample_run"),
            "--provider",
            "gemini",
            "--docs",
            str(repo / "docs" / "sandbox_design.md"),
            "--out",
            str(out),
        ],
    )
    if isinstance(result.exception, NotImplementedError):
        raise result.exception
    assert result.exit_code in (0, 3), result.output
    bugs = json.loads((out / "bugs.json").read_text(encoding="utf-8"))
    assert bugs and all(first_error("bug_report", b) is None for b in bugs)
    assert {b["generator"]["provider"] for b in bugs} == {"gemini"}
    assert {b["generator"]["model"] for b in bugs} == {DEFAULT_MODEL}
    meta = json.loads((out / "triage_meta.json").read_text(encoding="utf-8"))
    assert meta["provider"] == "gemini" and meta["tokens_in"] > 0
    assert any(call[0] == "embed_content" for call in models.calls)
