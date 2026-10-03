"""``qalab.visual_finding/1``: one line of ``visual_findings.jsonl`` (spec 03, written in M6)."""

from __future__ import annotations

from typing import Literal

from pydantic import BaseModel, ConfigDict, Field

from qalab.models.labels import Vec3, VisualLabel


class FoundLabel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    label: VisualLabel
    score: float = Field(ge=0, le=1)
    region: tuple[float, float, float, float] | None = None  # normalized x, y, w, h


class VisualFinding(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_: Literal["qalab.visual_finding/1"] = Field(
        default="qalab.visual_finding/1", alias="schema"
    )
    run_id: str
    shot: str
    t: float | None = None
    scene: str | None = None
    pos: Vec3 | None = None
    method: Literal["heuristic", "vlm", "ml", "hybrid"]
    labels: list[FoundLabel]
    explanation: str | None = None
    model: str | None = None
    latency_ms: float | None = Field(default=None, ge=0)
    cached: bool | None = None

    def to_json_dict(self) -> dict:
        return self.model_dump(by_alias=True, exclude_none=True, mode="json")
