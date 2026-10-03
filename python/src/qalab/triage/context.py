"""Spec 02 §7: build the per-cluster context the LLM prompt and the template report consume.

IDs (E1…, A1…, L1…, D1…) are local to one prompt; ``ClusterContext.resolve`` maps E and A ids
back to ``{run_id, seq}`` and ``doc_chunk_id`` maps D ids back to chunk ids.
"""

from __future__ import annotations

import json
import re
from collections.abc import Mapping, Sequence
from dataclasses import asdict, dataclass, field
from typing import Any, Protocol

from qalab.io.runs import LoadedRun
from qalab.models.bug import EventRef
from qalab.models.event import Event
from qalab.triage.cluster import Cluster, ClusterMember

MAX_EVIDENCE = 5
MAX_ACTIONS = 10
MAX_LOGS = 5
MAX_DOCS = 3
MESSAGE_CHARS = 300
DOC_CHARS = 600
FRAMES_PER_EVENT = 6
DEFAULT_BUDGET_CHARS = 10_000

# "The given key 'enemy_4f2a9c1e' was" → {"the", "given", "key", "enemy_4f2a9c1e", "was"}
_TOKEN = re.compile(r"\w+")


class RetrievedDoc(Protocol):
    """What ``qalab.rag.retrieve`` hands over: one chunk and its score."""

    chunk_id: str  # "sandbox_design.md#doors-0"
    source: str  # file name
    heading: str
    text: str
    score: float


@dataclass
class EvidenceItem:
    id: str
    run_id: str
    seq: int
    t: float
    scene: str | None
    level: str | None
    message: str
    frames: list[str]

    @property
    def chars(self) -> int:
        return len(self.message) + sum(len(f) + 4 for f in self.frames) + 40


@dataclass
class ActionItem:
    id: str
    run_id: str
    seq: int
    step: int
    t: float
    action: str
    detail: str  # "target=(4.0, 0.0, 2.0)" / "ui=Canvas/Menu/Play" / "object=Door_02"

    @property
    def chars(self) -> int:
        return len(self.action) + len(self.detail) + 30


@dataclass
class LogItem:
    id: str
    run_id: str
    seq: int
    t: float
    level: str
    message: str

    @property
    def chars(self) -> int:
        return len(self.message) + 30


@dataclass
class DocItem:
    id: str
    chunk_id: str
    source: str
    heading: str
    text: str

    @property
    def chars(self) -> int:
        return len(self.text) + len(self.heading) + len(self.source) + 10


@dataclass
class ClusterContext:
    facts: dict[str, Any]  # C
    events: list[EvidenceItem]  # E1–E5
    actions: list[ActionItem]  # A1–A10
    logs: list[LogItem]  # L1–L5
    docs: list[DocItem]  # D1–D3
    code: str | None  # CODE
    detector_details: dict[str, Any] | None
    screenshots: list[str]
    budget_chars: int
    trimmed: dict[str, int] = field(default_factory=dict)  # how many items each trim removed

    # ---- id mapping -------------------------------------------------------------------
    @property
    def event_ids(self) -> set[str]:
        return {e.id for e in self.events}

    @property
    def action_ids(self) -> set[str]:
        return {a.id for a in self.actions}

    @property
    def doc_ids(self) -> set[str]:
        return {d.id for d in self.docs}

    def resolve(self, prompt_id: str) -> EventRef:
        """``"E2"`` / ``"A3"`` / ``"L1"`` → the event it stands for. KeyError when unknown."""
        for items in (self.events, self.actions, self.logs):
            for item in items:
                if item.id == prompt_id:
                    return EventRef(run_id=item.run_id, seq=item.seq)
        raise KeyError(prompt_id)

    def doc_chunk_id(self, prompt_id: str) -> str:
        for doc in self.docs:
            if doc.id == prompt_id:
                return doc.chunk_id
        raise KeyError(prompt_id)

    # ---- size -------------------------------------------------------------------------
    @property
    def chars(self) -> int:
        total = len(json.dumps(self.facts)) + len(self.code or "")
        total += len(json.dumps(self.detector_details)) if self.detector_details else 0
        for items in (self.events, self.actions, self.logs, self.docs):
            total += sum(item.chars for item in items)  # type: ignore[attr-defined]
        return total

    def to_prompt_vars(self) -> dict[str, Any]:
        """Variables for ``prompts/triage_v1.md``."""
        facts = dict(self.facts)
        if self.detector_details:
            facts["detector_details"] = self.detector_details
        return {
            "cluster_json": json.dumps(facts, indent=1),
            "events": [asdict(e) for e in self.events],
            "actions": [asdict(a) for a in self.actions],
            "logs": [asdict(line) for line in self.logs],
            "docs": [asdict(d) for d in self.docs],
            "code": self.code,
        }


# ---- building ---------------------------------------------------------------------------


def build_context(
    cluster: Cluster,
    runs: Mapping[str, LoadedRun],
    docs: Sequence[RetrievedDoc] = (),
    code: str | None = None,
    budget_chars: int = DEFAULT_BUDGET_CHARS,
) -> ClusterContext:
    """Assemble C/E/A/L/D/CODE for one cluster and trim to the budget (E, then L, then D)."""
    first = cluster.first
    first_run = runs[first.event.run_id]
    context = ClusterContext(
        facts=cluster_facts(cluster, runs),
        events=evidence_items(cluster),
        actions=action_items(first_run.events, first.event),
        logs=log_items(first_run.events, first.event, cluster),
        docs=doc_items(docs),
        code=code,
        detector_details=cluster.detector_details,
        screenshots=cluster.screenshots,
        budget_chars=budget_chars,
    )
    trim_to_budget(context)
    return context


def cluster_facts(cluster: Cluster, runs: Mapping[str, LoadedRun]) -> dict[str, Any]:
    builds = sorted({runs[r].run.build_label for r in cluster.run_ids if r in runs})
    crash = any(runs[r].suspected_crash for r in cluster.run_ids if r in runs)
    facts: dict[str, Any] = {
        "signature": cluster.signature,
        "kind": cluster.kind,
        "level": cluster.level,
        "exception_type": cluster.exception_type or None,
        "detector": cluster.detector,
        "detector_severity": cluster.detector_severity,
        "count": cluster.count,
        "runs": cluster.runs_affected,
        "scenes": cluster.scenes,
        "builds": builds,
        "first_t": cluster.first_t,
        "last_t": cluster.last_t,
        "suspected_crash": crash,
        "top_frame": cluster.top_frame.qualified if cluster.top_frame else None,
    }
    return {k: v for k, v in facts.items() if v not in (None, "", [])}


def evidence_items(cluster: Cluster) -> list[EvidenceItem]:
    """First, last, then up to 3 members whose raw messages differ most from those picked."""
    members = cluster.members
    chosen: list[ClusterMember] = [members[0]]
    if members[-1] is not members[0]:
        chosen.append(members[-1])
    pool = [m for m in members if m not in chosen]
    while pool and len(chosen) < MAX_EVIDENCE:
        # Greedy max-min: the candidate farthest from everything already chosen.
        best = max(pool, key=lambda m: (min(_distance(m, c) for c in chosen), -m.event.seq))
        chosen.append(best)
        pool.remove(best)
    chosen.sort(key=lambda m: m.sort_key)
    return [_evidence_item(f"E{i}", m) for i, m in enumerate(chosen, start=1)]


def _distance(a: ClusterMember, b: ClusterMember) -> float:
    """1 − Jaccard similarity of message tokens: 0 for identical, 1 for nothing in common."""
    ta = set(_TOKEN.findall((a.event.message or "").lower()))
    tb = set(_TOKEN.findall((b.event.message or "").lower()))
    if not ta and not tb:
        return 0.0
    return 1.0 - len(ta & tb) / len(ta | tb)


def _evidence_item(prompt_id: str, member: ClusterMember) -> EvidenceItem:
    event = member.event
    message = event.message or _detector_message(event)
    return EvidenceItem(
        id=prompt_id,
        run_id=event.run_id,
        seq=event.seq,
        t=event.t,
        scene=event.scene,
        level=event.level,
        message=message[:MESSAGE_CHARS],
        frames=[str(f) for f in member.frames[:FRAMES_PER_EVENT]],
    )


def _detector_message(event: Event) -> str:
    data = event.data or {}
    details = json.dumps(data.get("details", {}), sort_keys=True)
    return f"{data.get('detector')} ({data.get('severity')}) at pos={event.pos} {details}"


def action_items(events: Sequence[Event], first: Event) -> list[ActionItem]:
    """The last 10 bot actions before the first occurrence, oldest first (A1 = oldest)."""
    before = [e for e in events if e.kind == "action" and e.seq < first.seq]
    recent = before[-MAX_ACTIONS:]
    return [
        ActionItem(
            id=f"A{i}",
            run_id=e.run_id,
            seq=e.seq,
            step=e.action().step,
            t=e.t,
            action=e.action().action,
            detail=action_detail(e),
        )
        for i, e in enumerate(recent, start=1)
    ]


def action_detail(event: Event) -> str:
    """Target, UI path or object of a bot action, whichever it has: "object=Door_02"."""
    data = event.action()
    if data.ui_path:
        return f"ui={data.ui_path}"
    if data.target is not None:
        return f"target=({data.target[0]:g}, {data.target[1]:g}, {data.target[2]:g})"
    if data.args:
        return " ".join(f"{k}={v}" for k, v in sorted(data.args.items()))
    return ""


def log_items(events: Sequence[Event], first: Event, cluster: Cluster) -> list[LogItem]:
    """The last 5 other log lines before the first occurrence (members excluded)."""
    member_seqs = {m.event.seq for m in cluster.members if m.event.run_id == first.run_id}
    before = [
        e for e in events if e.kind == "log" and e.seq < first.seq and e.seq not in member_seqs
    ]
    return [
        LogItem(
            id=f"L{i}",
            run_id=e.run_id,
            seq=e.seq,
            t=e.t,
            level=e.level or "info",
            message=(e.message or "")[:MESSAGE_CHARS],
        )
        for i, e in enumerate(before[-MAX_LOGS:], start=1)
    ]


def doc_items(docs: Sequence[RetrievedDoc]) -> list[DocItem]:
    return [
        DocItem(
            id=f"D{i}",
            chunk_id=d.chunk_id,
            source=d.source,
            heading=d.heading,
            text=d.text[:DOC_CHARS],
        )
        for i, d in enumerate(docs[:MAX_DOCS], start=1)
    ]


def trim_to_budget(context: ClusterContext) -> None:
    """Drop items from the end until the context fits: E first (keep E1), then L, then D."""
    while context.chars > context.budget_chars:
        if len(context.events) > 1:
            context.events.pop()
            context.trimmed["events"] = context.trimmed.get("events", 0) + 1
        elif context.logs:
            context.logs.pop()
            context.trimmed["logs"] = context.trimmed.get("logs", 0) + 1
        elif context.docs:
            context.docs.pop()
            context.trimmed["docs"] = context.trimmed.get("docs", 0) + 1
        else:
            break
