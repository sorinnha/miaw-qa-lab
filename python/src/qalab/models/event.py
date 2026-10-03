"""``qalab.event/1``: one line of ``events.jsonl``."""

from __future__ import annotations

from typing import Annotated, Any, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints, model_validator

Kind = Literal["log", "action", "detector", "metric", "screenshot", "marker"]
Level = Literal["info", "warning", "error", "exception", "assert"]
DetectorSeverity = Literal["blocker", "critical", "major", "minor", "trivial"]
Vec3 = tuple[float, float, float]


class _Strict(BaseModel):
    """Base for payload models: unknown keys are errors, like the schemas' additionalProperties."""

    model_config = ConfigDict(extra="forbid")


class ActionData(_Strict):
    action: str
    step: int = Field(ge=0)
    adapter: str | None = None
    target: Vec3 | None = None
    ui_path: str | None = None
    args: dict[str, Any] | None = None


class DetectorData(_Strict):
    detector: Annotated[str, StringConstraints(pattern=r"^[a-z0-9_]+(:[a-z0-9_]+)?$")]
    severity: DetectorSeverity
    details: dict[str, Any] | None = None
    screenshot: str | None = None


class MetricData(BaseModel):
    """Numbers only; extra numeric keys are allowed by the schema."""

    model_config = ConfigDict(extra="allow")

    fps: float | None = Field(default=None, ge=0)
    frame_ms: float | None = Field(default=None, ge=0)
    frame_ms_p95: float | None = Field(default=None, ge=0)
    mem_mb: float | None = Field(default=None, ge=0)
    gc_mb: float | None = Field(default=None, ge=0)

    @model_validator(mode="after")
    def _extras_are_numbers(self) -> MetricData:
        for key, value in (self.model_extra or {}).items():
            if isinstance(value, bool) or not isinstance(value, int | float):
                raise ValueError(f"metric {key!r} must be a number")
        return self


class ScreenshotData(_Strict):
    path: str
    reason: Literal["periodic", "detector", "manual"]
    w: int | None = Field(default=None, ge=1)
    h: int | None = Field(default=None, ge=1)
    method: Literal["screen_capture", "camera_render"] | None = None


class MarkerData(_Strict):
    marker: Literal[
        "run_start", "run_end", "scene_loaded", "scene_unloaded", "bot_started", "bot_stopped"
    ]
    details: dict[str, Any] | None = None


_DATA_MODELS: dict[str, type[BaseModel]] = {
    "action": ActionData,
    "detector": DetectorData,
    "metric": MetricData,
    "screenshot": ScreenshotData,
    "marker": MarkerData,
}


class Event(BaseModel):
    """One event. ``data`` stays a dict (round-trips unchanged) but is validated per kind."""

    model_config = ConfigDict(extra="forbid")

    schema_: Literal["qalab.event/1"] = Field(alias="schema")
    run_id: str = Field(min_length=1)
    seq: int = Field(ge=0)
    t: float = Field(ge=0)
    ts: str | None = None  # ISO 8601; jsonschema checks the format, triage uses ``t``
    frame: int | None = Field(default=None, ge=0)
    kind: Kind
    level: Level | None = None
    message: str | None = None
    stack: str | None = None
    scene: str | None = None
    pos: Vec3 | None = None
    data: dict[str, Any] | None = None

    @model_validator(mode="after")
    def _check_kind_payload(self) -> Event:
        if self.kind == "log":
            if self.level is None or self.message is None:
                raise ValueError("log events need level and message")
            return self
        if self.data is None:
            raise ValueError(f"{self.kind} events need data")
        _DATA_MODELS[self.kind].model_validate(self.data)
        return self

    # Typed views of ``data``; each raises if called on the wrong kind.
    def action(self) -> ActionData:
        return ActionData.model_validate(self._payload("action"))

    def detector(self) -> DetectorData:
        return DetectorData.model_validate(self._payload("detector"))

    def screenshot(self) -> ScreenshotData:
        return ScreenshotData.model_validate(self._payload("screenshot"))

    def _payload(self, kind: str) -> dict[str, Any]:
        if self.kind != kind or self.data is None:
            raise ValueError(f"event {self.seq} is {self.kind}, not {kind}")
        return self.data

    def to_json_dict(self) -> dict[str, Any]:
        """Dump with the schema's key names (``schema`` not ``schema_``) and no None noise."""
        return self.model_dump(by_alias=True, exclude_none=True, mode="json")
