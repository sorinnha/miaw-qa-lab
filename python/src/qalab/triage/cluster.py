"""The typed ``Cluster`` that M2's signature/cluster/rank steps produce (spec 02 §4–6).

Everything downstream (context, LLM report, template report, outputs) consumes this type, so
M2 must fill it exactly as documented here. Members are kept sorted by ``(run_id, seq)``.
"""

from __future__ import annotations

from dataclasses import dataclass, field

from qalab.models.bug import BugKind, EventRef, Priority
from qalab.models.event import DetectorSeverity, Event, Level
from qalab.triage.stack import Frame, app_frames_of


@dataclass
class ClusterMember:
    """One event that belongs to the cluster, with its parsed app frames."""

    event: Event
    frames: list[Frame] = field(default_factory=list)  # app frames only, top first

    @classmethod
    def from_event(cls, event: Event) -> ClusterMember:
        return cls(event=event, frames=app_frames_of(event.stack))

    @property
    def ref(self) -> EventRef:
        return EventRef(run_id=self.event.run_id, seq=self.event.seq)

    @property
    def sort_key(self) -> tuple[str, int]:
        return (self.event.run_id, self.event.seq)


@dataclass
class Cluster:
    signature: str  # 12 hex chars (spec §4)
    kind: BugKind  # log | detector | visual
    members: list[ClusterMember]
    # log clusters
    level: Level | None = None
    exception_type: str = ""  # leading \w+(Exception|Error) of the message, else ""
    normalized_message: str = ""
    # detector / visual clusters
    detector: str | None = None  # e.g. "fell_out_of_world" or "visual:missing_texture"
    detector_severity: DetectorSeverity | None = None
    cell: tuple[int, int] | None = None  # (floor(x/4), floor(z/4)) of the first member
    # §5: signatures merged into this cluster by the variant (empty under ``exact``)
    merged_from: list[str] = field(default_factory=list)
    # §6: filled by rank.py; the LLM never changes these
    score: float = 0.0
    priority: Priority = "P4"
    score_breakdown: dict[str, float] = field(default_factory=dict)

    def __post_init__(self) -> None:
        if not self.members:
            raise ValueError("a cluster needs at least one member")
        self.members.sort(key=lambda m: m.sort_key)

    @property
    def first(self) -> ClusterMember:
        """First occurrence: lowest ``(run_id, seq)``; its run supplies the A and L lists."""
        return self.members[0]

    @property
    def count(self) -> int:
        return len(self.members)

    @property
    def run_ids(self) -> list[str]:
        return sorted({m.event.run_id for m in self.members})

    @property
    def runs_affected(self) -> int:
        return len(self.run_ids)

    @property
    def scenes(self) -> list[str]:
        return sorted({m.event.scene for m in self.members if m.event.scene})

    @property
    def first_t(self) -> float:
        return min(m.event.t for m in self.members)

    @property
    def last_t(self) -> float:
        return max(m.event.t for m in self.members)

    @property
    def top_frame(self) -> Frame | None:
        """First app frame of the first member that has one (the frame that names the bug)."""
        for member in self.members:
            if member.frames:
                return member.frames[0]
        return None

    @property
    def top_frames(self) -> list[str]:
        """f1–f3 of the first member, as used by the log signature."""
        return [f.qualified for f in self.first.frames[:3]]

    @property
    def label(self) -> str:
        """Short name for logs and titles: the exception type, the detector, or the level."""
        return self.exception_type or self.detector or (self.level or self.kind)

    @property
    def screenshots(self) -> list[str]:
        paths = [
            (m.event.data or {}).get("screenshot")
            for m in self.members
            if m.event.kind == "detector"
        ]
        return sorted({p for p in paths if p})

    @property
    def detector_details(self) -> dict | None:
        if self.first.event.kind != "detector":
            return None
        return (self.first.event.data or {}).get("details")
