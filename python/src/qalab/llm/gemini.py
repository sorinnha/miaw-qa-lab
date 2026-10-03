"""Gemini provider (hosted, optional ``[gemini]`` extra) on Google's official ``google-genai`` SDK.

- ``complete_json``: ``generate_content`` with structured output (``response_json_schema``),
  temperature 0 by default, and screenshots as inline image parts.
- ``embed``: ``embed_content`` with a Gemini embedding model; rows come back L2-normalized.
- The API key is read from ``GEMINI_API_KEY`` only. It is passed to the SDK client and never
  logged, stored on this object, or put in an error message. Prompts are never logged either.

Requests are built as plain dicts (the SDK accepts ``...Dict`` forms everywhere), so this module
imports ``google.genai`` only when it creates a real client. Tests inject a fake client and run
without the extra installed.
"""

from __future__ import annotations

import logging
import os
from typing import Any

import numpy as np

from qalab.llm.base import (
    LLMError,
    LLMOutputError,
    LLMResult,
    Timer,
    normalize_rows,
    parse_json_object,
)

log = logging.getLogger("qalab.llm")

API_KEY_ENV = "GEMINI_API_KEY"
DEFAULT_MODEL = "gemini-2.5-flash"
DEFAULT_EMBED_MODEL = "gemini-embedding-001"
INSTALL_HINT = 'pip install -e ".\\python[dev,gemini]"'

# JSON Schema keywords that describe the file rather than the data. Gemini's structured-output
# mode only needs the shape, so these are stripped before sending.
_SCHEMA_METADATA_KEYS = ("$schema", "$id", "title", "description")

# First bytes of common image formats → MIME type (screenshots are PNG; JPEG for safety).
_IMAGE_SIGNATURES = ((b"\x89PNG\r\n\x1a\n", "image/png"), (b"\xff\xd8\xff", "image/jpeg"))


def response_schema(schema: dict[str, Any]) -> dict[str, Any]:
    """The draft schema without file-level metadata (``$schema``, ``$id``, ``title``...)."""
    return {k: v for k, v in schema.items() if k not in _SCHEMA_METADATA_KEYS}


def image_mime_type(data: bytes) -> str:
    for signature, mime in _IMAGE_SIGNATURES:
        if data.startswith(signature):
            return mime
    return "image/png"


def build_contents(user: str, images: list[bytes] | None) -> list[dict[str, Any]]:
    """One user turn: the prompt text, then each image as an inline part."""
    parts: list[dict[str, Any]] = [{"text": user}]
    for img in images or []:
        parts.append({"inline_data": {"mime_type": image_mime_type(img), "data": img}})
    return [{"role": "user", "parts": parts}]


def build_config(system: str, schema: dict[str, Any], temperature: float) -> dict[str, Any]:
    """``GenerateContentConfig`` as a dict: system rules, temperature, JSON-schema output."""
    return {
        "system_instruction": system,
        "temperature": temperature,
        "response_mime_type": "application/json",
        "response_json_schema": response_schema(schema),
    }


def make_client(api_key: str | None = None) -> Any:
    """A real ``google.genai.Client``. Raises ValueError (CLI exit 2) when it can't be built."""
    key = api_key or os.environ.get(API_KEY_ENV)
    if not key:
        raise ValueError(
            f"{API_KEY_ENV} is not set: put it in .env (gitignored) or the environment"
        )
    try:
        from google import genai  # optional extra; imported only when actually used
    except ImportError as exc:
        raise ValueError(f"the gemini extra is not installed: {INSTALL_HINT}") from exc
    return genai.Client(api_key=key)


class GeminiProvider:
    name = "gemini"

    def __init__(
        self,
        model: str = DEFAULT_MODEL,
        embed_model: str = DEFAULT_EMBED_MODEL,
        client: Any | None = None,
    ) -> None:
        self.model = model
        self.embed_model = embed_model
        # Tests pass a fake client; production builds the SDK client from GEMINI_API_KEY.
        self._client = client if client is not None else make_client()

    def complete_json(
        self,
        system: str,
        user: str,
        schema: dict[str, Any],
        images: list[bytes] | None = None,
        temperature: float = 0.0,
    ) -> LLMResult:
        with Timer() as timer:
            response = self._call(
                "generate_content",
                model=self.model,
                contents=build_contents(user, images),
                config=build_config(system, schema, temperature),
            )
        raw = getattr(response, "text", None) or ""
        if not raw.strip():
            # Blocked or empty answers count as bad output, so the report layer retries.
            reason = _finish_reason(response)
            raise LLMOutputError(f"gemini returned no text (finish_reason={reason})", raw)
        tokens_in, tokens_out = _token_counts(response)
        return LLMResult(
            data=parse_json_object(raw),
            raw=raw,
            tokens_in=tokens_in,
            tokens_out=tokens_out,
            latency_ms=timer.ms,
        )

    def embed(self, texts: list[str]) -> np.ndarray:
        if not texts:
            return np.zeros((0, 0), dtype=np.float32)
        response = self._call("embed_content", model=self.embed_model, contents=list(texts))
        embeddings = getattr(response, "embeddings", None) or []
        if len(embeddings) != len(texts):
            raise LLMError("gemini embed response has the wrong number of embeddings")
        return normalize_rows(np.asarray([e.values for e in embeddings], dtype=np.float32))

    def _call(self, method: str, **kwargs: Any) -> Any:
        """Call ``client.models.<method>``; SDK/HTTP failures become ``LLMError``.

        The error text carries the exception type and HTTP code only: never the prompt, and the
        key is not part of either.
        """
        try:
            return getattr(self._client.models, method)(**kwargs)
        except LLMError:
            raise
        except Exception as exc:  # the SDK raises several types (APIError, httpx errors, ...)
            code = getattr(exc, "code", None)
            detail = f"HTTP {code}" if code else type(exc).__name__
            raise LLMError(f"gemini {method} failed: {detail}") from exc


def _token_counts(response: Any) -> tuple[int, int]:
    """Prompt tokens in; answer + thinking tokens out (both are billed as output)."""
    usage = getattr(response, "usage_metadata", None)
    if usage is None:
        return 0, 0
    tokens_in = getattr(usage, "prompt_token_count", None) or 0
    answer = getattr(usage, "candidates_token_count", None) or 0
    thinking = getattr(usage, "thoughts_token_count", None) or 0
    return int(tokens_in), int(answer + thinking)


def _finish_reason(response: Any) -> str:
    candidates = getattr(response, "candidates", None) or []
    if not candidates:
        return "no candidates"
    return str(getattr(candidates[0], "finish_reason", None) or "unknown")
