"""Spec 03 VLM classifier: one screenshot → labels, through any ``LLMProvider`` that takes images.

Structured output (``schemas/llm_vision.schema.json``) at temperature 0. ``CachedProvider`` keys on
the prompt text (which names the screenshot path), the image bytes' hash, the model and the prompt
version, so re-running a run is free; the same pixels under another path are a new call.
"""

from __future__ import annotations

from dataclasses import dataclass
from typing import Literal

import numpy as np
from pydantic import BaseModel, ConfigDict, Field, ValidationError

from qalab.llm.base import LLMError, LLMProvider
from qalab.models.schemas import load_schema
from qalab.models.visual import FoundLabel
from qalab.triage.prompts import load_prompt
from qalab.vision.images import downscale, to_png

VLM_LONG_SIDE = 768
PROMPT = "vision_v1"


class _Verdict(BaseModel):
    model_config = ConfigDict(extra="forbid")

    class Item(BaseModel):
        model_config = ConfigDict(extra="forbid")

        label: Literal["missing_texture", "black_screen", "ui_overflow", "placeholder_ui"]
        score: float

    labels: list[Item]
    explanation: str = Field(default="")


@dataclass
class VlmVerdict:
    labels: list[FoundLabel]
    explanation: str
    model: str
    latency_ms: float
    cached: bool
    tokens: int
    error: str | None = None  # set when the model's answer was unusable (counted, not raised)


def analyze(
    rgb: np.ndarray,
    provider: LLMProvider,
    shot: str,
    scene: str | None,
    t: float | None,
    max_retries: int = 2,
) -> VlmVerdict:
    """Ask the model about one frame. Bad JSON is retried; after that the frame gets no labels and
    ``error`` says why (a vision failure must not stop the analysis of the other frames)."""
    prompt = load_prompt(PROMPT)
    image = to_png(downscale(rgb, VLM_LONG_SIDE))
    user = prompt.render_user(
        shot=shot, scene=scene or "unknown", t=f"{t:.1f}" if t is not None else "?"
    )
    schema = load_schema("llm_vision")
    last_error = "no attempt"
    latency = 0.0
    for _ in range(max_retries + 1):
        try:
            result = provider.complete_json(
                prompt.render_system(), user, schema, images=[image], temperature=0.0
            )
        except LLMError as exc:
            last_error = str(exc)
            continue
        latency += result.latency_ms
        try:
            verdict = _Verdict.model_validate(result.data)
        except ValidationError as exc:
            last_error = f"invalid verdict: {exc.errors()[0]['msg']}"
            continue
        labels: dict[str, FoundLabel] = {}
        for item in verdict.labels:
            score = min(1.0, max(0.0, item.score))
            if item.label not in labels or labels[item.label].score < score:
                labels[item.label] = FoundLabel(label=item.label, score=round(score, 4))
        return VlmVerdict(
            labels=list(labels.values()),
            explanation=verdict.explanation[:300],
            model=provider.model,
            latency_ms=round(latency, 1),
            cached=result.cached,
            tokens=result.tokens_in + result.tokens_out,
        )
    return VlmVerdict([], "", provider.model, round(latency, 1), False, 0, error=last_error)
