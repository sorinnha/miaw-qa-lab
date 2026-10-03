"""Ollama provider: local chat with JSON-schema ``format`` and the ``/api/embed`` endpoint."""

from __future__ import annotations

import base64
import os
from typing import Any

import httpx
import numpy as np

from qalab.llm.base import LLMError, LLMResult, Timer, normalize_rows, parse_json_object

DEFAULT_HOST = "http://localhost:11434"
DEFAULT_TIMEOUT_S = 120.0


class OllamaProvider:
    name = "ollama"

    def __init__(
        self,
        model: str,
        embed_model: str | None = None,
        host: str | None = None,
        timeout_s: float = DEFAULT_TIMEOUT_S,
        client: httpx.Client | None = None,
    ) -> None:
        self.model = model
        self.embed_model = embed_model or model
        self.host = (host or os.environ.get("OLLAMA_HOST") or DEFAULT_HOST).rstrip("/")
        # Tests inject a client built on httpx.MockTransport, so nothing reaches the network.
        self._client = client or httpx.Client(timeout=timeout_s)

    def complete_json(
        self,
        system: str,
        user: str,
        schema: dict[str, Any],
        images: list[bytes] | None = None,
        temperature: float = 0.0,
    ) -> LLMResult:
        user_message: dict[str, Any] = {"role": "user", "content": user}
        if images:
            user_message["images"] = [base64.b64encode(img).decode("ascii") for img in images]
        body = {
            "model": self.model,
            "messages": [{"role": "system", "content": system}, user_message],
            "stream": False,
            "format": schema,
            "options": {"temperature": temperature},
        }
        with Timer() as timer:
            payload = self._post("/api/chat", body)
        try:
            raw = payload["message"]["content"]
        except (KeyError, TypeError) as exc:
            raise LLMError("ollama chat response has no message.content") from exc
        return LLMResult(
            data=parse_json_object(raw),
            raw=raw,
            tokens_in=int(payload.get("prompt_eval_count", 0)),
            tokens_out=int(payload.get("eval_count", 0)),
            latency_ms=timer.ms,
        )

    def embed(self, texts: list[str]) -> np.ndarray:
        if not texts:
            return np.zeros((0, 0), dtype=np.float32)
        payload = self._post("/api/embed", {"model": self.embed_model, "input": texts})
        vectors = payload.get("embeddings")
        if not isinstance(vectors, list) or len(vectors) != len(texts):
            raise LLMError("ollama embed response has the wrong number of embeddings")
        return normalize_rows(np.asarray(vectors, dtype=np.float32))

    def _post(self, path: str, body: dict[str, Any]) -> dict[str, Any]:
        try:
            response = self._client.post(f"{self.host}{path}", json=body)
            response.raise_for_status()
            return response.json()
        except httpx.HTTPStatusError as exc:
            raise LLMError(f"ollama {path} failed: HTTP {exc.response.status_code}") from exc
        except httpx.HTTPError as exc:
            raise LLMError(f"ollama {path} unreachable at {self.host}: {exc}") from exc
        except ValueError as exc:
            raise LLMError(f"ollama {path} returned invalid JSON") from exc
