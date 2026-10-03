"""``qalab.run/1``: ``run.json`` metadata."""

from __future__ import annotations

from datetime import datetime
from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints


class Build(BaseModel):
    model_config = ConfigDict(extra="forbid")

    version: str | None = None
    platform: str | None = None
    unity: str | None = None
    git_sha: str | None = None
    development: bool | None = None


class Machine(BaseModel):
    model_config = ConfigDict(extra="forbid")

    os: str | None = None
    cpu: str | None = None
    gpu: str | None = None
    ram_gb: float | None = None


class Run(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_: Literal["qalab.run/1"] = Field(alias="schema")
    run_id: Annotated[str, StringConstraints(pattern=r"^[A-Za-z0-9_.-]+$")]
    project: str
    qalab_version: str | None = None
    build: Build | None = None
    mode: Literal["player", "editor_playmode", "manual"]
    adapter: str | None = None
    seed: int
    scenes: list[str] = Field(default_factory=list)
    duration_s: float | None = Field(default=None, ge=0)
    started_at: datetime
    ended_at: datetime | None = None
    exit_reason: (
        Literal["duration_elapsed", "fatal_detector", "user_quit", "test_finished", "unknown"]
        | None
    ) = None
    exit_code: int | None = None
    machine: Machine | None = None
    benchmark: bool | None = None
    seeds_enabled: list[Annotated[str, StringConstraints(pattern=r"^SB[0-9]{2}$")]] = Field(
        default_factory=list
    )

    @property
    def suspected_crash(self) -> bool:
        """A run that never rewrote ``ended_at`` did not finish cleanly (spec 02 §1)."""
        return self.ended_at is None

    @property
    def build_label(self) -> str:
        """``version/platform`` for the report's environment block."""
        if self.build is None:
            return "unknown"
        return f"{self.build.version or '?'}/{self.build.platform or '?'}"
