"""Deterministic provider for tests and CI. Never touches the network."""

from __future__ import annotations

import hashlib
import json
import re
from typing import Any

import numpy as np

from qalab.llm.base import (
    LLMOutputError,
    LLMResult,
    Timer,
    estimate_tokens,
    normalize_rows,
    parse_json_object,
)

# "[E1] t=8.21s ..." → "E1"; "[A3] step 3 ..." → "A3"; "[D2] file > heading" → "D2"
_PROMPT_ID = re.compile(r"^\[(?P<id>[EAD]\d+)\]", re.MULTILINE)
# "[A3] step 3 t=8.2s interact object=Door_02" → "interact object=Door_02"
_ACTION_LINE = re.compile(r"^\[(?P<id>A\d+)\] step \d+ t=[\d.]+s (?P<text>.*)$", re.MULTILINE)
# "door opens slowly" → ["door", "opens", "slowly"]
_WORD = re.compile(r"\w+")

EMBED_DIM = 256


class FakeProvider:
    """Builds a valid draft from the prompt: first E id as evidence, every A id as a bot_log step.

    ``invalid_json_times``: how many calls answer with broken JSON first (to test retries).
    ``overrides``: fields forced into every draft (e.g. ``{"severity": "S4"}``) for grounding tests.
    ``script``: raw texts to return verbatim, in order, before falling back to the built draft.
    """

    name = "fake"

    def __init__(
        self,
        model: str = "fake-1",
        invalid_json_times: int = 0,
        overrides: dict[str, Any] | None = None,
        script: list[str] | None = None,
    ) -> None:
        self.model = model
        self.embed_model = model
        self.invalid_json_times = invalid_json_times
        self.overrides = overrides or {}
        self.script = list(script or [])
        self.calls: list[dict[str, Any]] = []  # every request, for assertions
        self.embed_calls: list[list[str]] = []

    def complete_json(
        self,
        system: str,
        user: str,
        schema: dict[str, Any],
        images: list[bytes] | None = None,
        temperature: float = 0.0,
    ) -> LLMResult:
        self.calls.append({"system": system, "user": user, "images": len(images or [])})
        with Timer() as timer:
            if self.script:
                raw = self.script.pop(0)
            elif self.invalid_json_times > 0:
                self.invalid_json_times -= 1
                raw = '{"title": "broken", '  # truncated on purpose
            else:
                raw = json.dumps(self.build_draft(user))
        data = parse_json_object(raw)  # raises LLMOutputError for the broken answers
        return LLMResult(
            data=data,
            raw=raw,
            tokens_in=estimate_tokens(system) + estimate_tokens(user),
            tokens_out=estimate_tokens(raw),
            latency_ms=timer.ms,
        )

    def build_draft(self, user: str) -> dict[str, Any]:
        ids = _PROMPT_ID.findall(user)
        e_ids = [i for i in ids if i.startswith("E")]
        d_ids = [i for i in ids if i.startswith("D")]
        steps = [
            {
                "text": m.group("text").strip() or m.group("id"),
                "source": "bot_log",
                "action_ids": [m.group("id")],
            }
            for m in _ACTION_LINE.finditer(user)
        ] or [
            {"text": "Play the run with the recorded seed", "source": "inferred", "action_ids": []}
        ]
        draft: dict[str, Any] = {
            "title": "Fake: issue reproduced from recorded evidence",
            "component": "unknown",
            "severity": "S2",
            "summary": "Fake provider summary built from the prompt context.",
            "steps_to_reproduce": steps,
            "expected": "unknown",
            "actual": "See evidence.",
            "suspected_cause": "unknown",
            "evidence_ids": e_ids[:1],
            "doc_ids": d_ids,
            "confidence": 0.9,
        }
        draft.update(self.overrides)
        return draft

    def embed(self, texts: list[str]) -> np.ndarray:
        """Hashed bag-of-words: each word adds 1 to a bucket chosen by its sha1 hash."""
        self.embed_calls.append(list(texts))
        matrix = np.zeros((len(texts), EMBED_DIM), dtype=np.float32)
        for row, text in enumerate(texts):
            for word in _WORD.findall(text.lower()):
                digest = hashlib.sha1(word.encode("utf-8")).digest()
                matrix[row, int.from_bytes(digest[:4], "big") % EMBED_DIM] += 1.0
        return normalize_rows(matrix)


__all__ = ["FakeProvider", "LLMOutputError"]
