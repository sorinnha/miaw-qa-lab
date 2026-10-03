"""``qalab.labels/1``: ground truth for benchmark runs. Imported by ``qalab.eval`` only."""

from __future__ import annotations

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints

VisualLabel = Literal["missing_texture", "black_screen", "ui_overflow", "placeholder_ui"]
Vec3 = tuple[float, float, float]
BugId = Annotated[str, StringConstraints(pattern=r"^SB[0-9]{2}$")]


class _Strict(BaseModel):
    model_config = ConfigDict(extra="forbid")


class MatchRule(_Strict):
    """Every key given must match an event (spec 00)."""

    stack_contains: str | None = None
    message_regex: str | None = None
    detector: str | None = None
    near: Vec3 | None = None
    radius: float | None = Field(default=None, gt=0)
    visual_label: VisualLabel | None = None


class Trigger(_Strict):
    t: float = Field(ge=0)
    scene: str | None = None


class SeededBug(_Strict):
    bug_id: BugId
    type: Literal["log", "detector", "visual"]
    title: str | None = None
    feature: str  # exact H2 heading in docs/sandbox_design.md
    expected_severity: Literal["S1", "S2", "S3", "S4"]
    match: MatchRule
    triggers: list[Trigger] | None = None


class ShotLabel(_Strict):
    path: str
    t: float | None = Field(default=None, ge=0)
    labels: list[VisualLabel]  # empty = clean frame
    bug_ids: list[BugId] | None = None


class Labels(_Strict):
    schema_: Literal["qalab.labels/1"] = Field(alias="schema")
    run_id: str
    seeded_bugs: list[SeededBug]
    screenshots: list[ShotLabel]
