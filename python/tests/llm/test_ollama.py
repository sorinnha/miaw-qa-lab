"""Ollama client against httpx.MockTransport: no network, exact request bodies checked."""

import base64
import json

import httpx
import numpy as np
import pytest

from qalab.llm import LLMError, LLMOutputError
from qalab.llm.ollama import OllamaProvider

SCHEMA = {"type": "object", "properties": {"a": {"type": "integer"}}, "required": ["a"]}


def _provider(handler) -> OllamaProvider:
    client = httpx.Client(transport=httpx.MockTransport(handler))
    return OllamaProvider(
        model="test-llm", embed_model="test-embed", host="http://ollama.test", client=client
    )


def test_chat_request_and_response() -> None:
    seen: list[httpx.Request] = []

    def handler(request: httpx.Request) -> httpx.Response:
        seen.append(request)
        return httpx.Response(
            200,
            json={
                "message": {"role": "assistant", "content": '{"a": 1}'},
                "prompt_eval_count": 120,
                "eval_count": 7,
            },
        )

    result = _provider(handler).complete_json("SYS", "USER", SCHEMA, images=[b"\x89PNG"])
    assert result.data == {"a": 1} and result.tokens_in == 120 and result.tokens_out == 7
    assert result.cached is False and result.latency_ms >= 0

    request = seen[0]
    assert request.url == "http://ollama.test/api/chat"
    body = json.loads(request.content)
    assert body["model"] == "test-llm" and body["stream"] is False
    assert body["format"] == SCHEMA and body["options"] == {"temperature": 0.0}
    assert body["messages"][0] == {"role": "system", "content": "SYS"}
    assert body["messages"][1]["content"] == "USER"
    assert body["messages"][1]["images"] == [base64.b64encode(b"\x89PNG").decode()]


def test_chat_without_images_omits_field_and_passes_temperature() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        body = json.loads(request.content)
        assert "images" not in body["messages"][1]
        assert body["options"]["temperature"] == 0.3
        return httpx.Response(200, json={"message": {"content": "{}"}})

    assert _provider(handler).complete_json("s", "u", SCHEMA, temperature=0.3).data == {}


def test_non_json_answer_is_an_output_error() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"message": {"content": "Sorry, I cannot."}})

    with pytest.raises(LLMOutputError) as exc:
        _provider(handler).complete_json("s", "u", SCHEMA)
    assert exc.value.raw == "Sorry, I cannot."


def test_http_errors_become_llm_errors() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(500, text="boom")

    with pytest.raises(LLMError, match="HTTP 500"):
        _provider(handler).complete_json("s", "u", SCHEMA)

    def down(request: httpx.Request) -> httpx.Response:
        raise httpx.ConnectError("refused")

    with pytest.raises(LLMError, match="unreachable"):
        _provider(down).embed(["x"])


def test_embed_request_and_normalization() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        assert request.url.path == "/api/embed"
        body = json.loads(request.content)
        assert body == {"model": "test-embed", "input": ["a", "b"]}
        return httpx.Response(200, json={"embeddings": [[3.0, 4.0], [0.0, 2.0]]})

    matrix = _provider(handler).embed(["a", "b"])
    assert matrix.dtype == np.float32
    np.testing.assert_allclose(matrix, [[0.6, 0.8], [0.0, 1.0]])
    assert _provider(handler).embed([]).shape == (0, 0)


def test_embed_count_mismatch_is_an_error() -> None:
    def handler(request: httpx.Request) -> httpx.Response:
        return httpx.Response(200, json={"embeddings": [[1.0]]})

    with pytest.raises(LLMError, match="wrong number"):
        _provider(handler).embed(["a", "b"])


def test_host_comes_from_env(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("OLLAMA_HOST", "http://box:11434/")
    assert OllamaProvider(model="m").host == "http://box:11434"
