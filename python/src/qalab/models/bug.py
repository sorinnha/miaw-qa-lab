"""``qalab.bug_report/1`` (final report) and ``qalab.llm_bug_draft/1`` (what the LLM returns)."""

from __future__ import annotations

from typing import Annotated, Literal

from pydantic import BaseModel, ConfigDict, Field, StringConstraints

Severity = Literal["S1", "S2", "S3", "S4"]
Priority = Literal["P1", "P2", "P3", "P4"]
BugKind = Literal["log", "detector", "visual"]


class _Strict(BaseModel):
    model_config = ConfigDict(extra="forbid")


class EventRef(_Strict):
    """Points at one line of one run's ``events.jsonl``."""

    run_id: str
    seq: int = Field(ge=0)


class Step(_Strict):
    text: str
    source: Literal["bot_log", "inferred", "template"]
    action_refs: list[EventRef] | None = None


class Frequency(_Strict):
    count: int = Field(ge=1)
    runs: int = Field(ge=1)
    first_seen_t: float | None = Field(default=None, ge=0)
    last_seen_t: float | None = Field(default=None, ge=0)


class Environment(_Strict):
    builds: list[str] | None = None
    platforms: list[str] | None = None
    scenes: list[str] | None = None


class Generator(_Strict):
    method: Literal["llm", "template"]
    provider: str | None = None
    model: str | None = None
    prompt_version: str | None = None
    latency_ms: float | None = Field(default=None, ge=0)
    tokens_in: int | None = Field(default=None, ge=0)
    tokens_out: int | None = Field(default=None, ge=0)
    cached: bool | None = None
    attempts: int | None = Field(default=None, ge=1)


class BugReport(_Strict):
    schema_: Literal["qalab.bug_report/1"] = Field(default="qalab.bug_report/1", alias="schema")
    id: Annotated[str, StringConstraints(pattern=r"^QAL-[0-9]{4}$")]
    signature: Annotated[str, StringConstraints(pattern=r"^[0-9a-f]{12}$")]
    kind: BugKind
    title: str = Field(max_length=100)
    component: str
    severity: Severity
    priority: Priority
    score: float = Field(ge=0)
    score_breakdown: dict[str, float] | None = None
    summary: str
    steps_to_reproduce: list[Step]
    expected: str
    actual: str
    suspected_cause: str
    frequency: Frequency
    environment: Environment
    evidence: list[EventRef] = Field(min_length=1)
    attachments: list[str] | None = None
    docs_used: list[str] | None = None
    confidence: float = Field(ge=0, le=1)
    needs_review: bool
    review_reasons: list[str] | None = None
    generator: Generator

    model_config = ConfigDict(extra="forbid", populate_by_name=True)

    def to_json_dict(self) -> dict:
        return self.model_dump(by_alias=True, exclude_none=True, mode="json")


class DraftStep(_Strict):
    text: str
    source: Literal["bot_log", "inferred"]
    action_ids: list[str]


class LLMBugDraft(_Strict):
    """Structured output requested from the model. Every field is required, no extras."""

    title: str
    component: str
    severity: Severity
    summary: str
    steps_to_reproduce: list[DraftStep]
    expected: str
    actual: str
    suspected_cause: str
    evidence_ids: list[str]
    doc_ids: list[str]
    confidence: float
