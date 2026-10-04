"""The typed ``Cluster`` that M2's signature/cluster/rank steps produce (spec 02 §4–6).

Everything downstream (context, LLM report, template report, outputs) consumes this type, so
M2 must fill it exactly as documented here. Members are kept sorted by ``(run_id, seq)``.
"""

from __future__ import annotations

import logging
from collections.abc import Mapping
from dataclasses import dataclass, field
from typing import Literal

import numpy as np
from sklearn.cluster import DBSCAN
from sklearn.feature_extraction.text import TfidfVectorizer

from qalab.io.runs import LoadedRun
from qalab.llm.base import LLMProvider
from qalab.models.bug import BugKind, Priority
from qalab.models.event import DetectorSeverity, Event, Level
from qalab.triage.signature import detector_signature, is_candidate, log_signature
from qalab.triage.stack import Frame, app_frames_of

log = logging.getLogger(__name__)

Variant = Literal["exact", "frame_tfidf", "frame_embed", "tfidf_only"]
VARIANTS: tuple[Variant, ...] = ("exact", "frame_tfidf", "frame_embed", "tfidf_only")
# Highest first: when variants merge clusters of different levels the keeper takes the worst.
LEVEL_ORDER = ("exception", "assert", "error", "warning", "info")


@dataclass
class ClusterMember:
    """One event that belongs to the cluster, with its parsed app frames."""

    event: Event
    frames: list[Frame] = field(default_factory=list)  # app frames only, top first

    @classmethod
    def from_event(cls, event: Event) -> ClusterMember:
        return cls(event=event, frames=app_frames_of(event.stack))

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


# ---- building clusters (spec 02 §4–5) -------------------------------------------------------
#
# ``exact`` groups candidate events by signature; the other variants then merge log clusters
# with union-find in a deterministic order (sorted by signature) so results are reproducible.


class UnionFind:
    """Disjoint sets over indices; ``find`` returns the root, ``union`` joins two sets."""

    def __init__(self, n: int) -> None:
        self.parent = list(range(n))

    def find(self, i: int) -> int:
        while self.parent[i] != i:
            self.parent[i] = self.parent[self.parent[i]]
            i = self.parent[i]
        return i

    def union(self, a: int, b: int) -> None:
        ra, rb = self.find(a), self.find(b)
        if ra != rb:
            self.parent[max(ra, rb)] = min(ra, rb)


def exact_clusters(
    runs: Mapping[str, LoadedRun],
    min_level: str = "warning",
    cell_size_m: float = 4.0,
    dbscan_eps_m: float = 3.0,
) -> list[Cluster]:
    """One cluster per signature; detector cells of one detector+scene merged with DBSCAN."""
    by_signature: dict[str, Cluster] = {}
    for loaded in runs.values():
        for event in loaded.events:
            if not is_candidate(event, min_level):
                continue
            if event.kind == "log":
                signature, exception_type, normalized, frames = log_signature(event)
                cluster = by_signature.get(signature)
                if cluster is None:
                    cluster = by_signature[signature] = Cluster(
                        signature=signature,
                        kind="log",
                        members=[ClusterMember.from_event(event)],
                        level=event.level,
                        exception_type=exception_type,
                        normalized_message=normalized,
                    )
                else:
                    cluster.members.append(ClusterMember.from_event(event))
            else:
                signature, detector, cell = detector_signature(event, cell_size_m)
                cluster = by_signature.get(signature)
                if cluster is None:
                    kind: BugKind = "visual" if detector.startswith("visual:") else "detector"
                    cluster = by_signature[signature] = Cluster(
                        signature=signature,
                        kind=kind,
                        members=[ClusterMember.from_event(event)],
                        detector=detector,
                        detector_severity=event.detector().severity,
                        cell=cell,
                    )
                else:
                    cluster.members.append(ClusterMember.from_event(event))
    clusters = [_sorted(c) for c in by_signature.values()]
    clusters = merge_detector_cells(clusters, dbscan_eps_m)
    return sorted(clusters, key=lambda c: c.signature)


def _sorted(cluster: Cluster) -> Cluster:
    cluster.members.sort(key=lambda m: m.sort_key)
    return cluster


def merge_detector_cells(clusters: list[Cluster], eps_m: float) -> list[Cluster]:
    """Neighbouring cells of the same detector and scene become one cluster (DBSCAN on x, z)."""
    groups: dict[tuple[str, str], list[Cluster]] = {}
    passthrough: list[Cluster] = []
    for cluster in clusters:
        if cluster.kind == "log":
            passthrough.append(cluster)
        else:
            scene = cluster.first.event.scene or ""
            groups.setdefault((cluster.detector or "", scene), []).append(cluster)
    merged: list[Cluster] = list(passthrough)
    for group in groups.values():
        if len(group) == 1:
            merged.extend(group)
            continue
        points = np.array(
            [[m.event.pos[0], m.event.pos[2]] for c in group for m in c.members if m.event.pos]
        )
        owners = [c for c in group for m in c.members if m.event.pos]
        if len(points) < 2:
            merged.extend(group)
            continue
        labels = DBSCAN(eps=eps_m, min_samples=1).fit_predict(points)
        uf = UnionFind(len(group))
        index = {id(c): i for i, c in enumerate(group)}
        for label in set(labels):
            same = [
                index[id(owner)] for owner, lab in zip(owners, labels, strict=True) if lab == label
            ]
            for other in same[1:]:
                uf.union(same[0], other)
        merged.extend(_apply_union(group, uf))
    return merged


def _apply_union(clusters: list[Cluster], uf: UnionFind) -> list[Cluster]:
    """Fold each union-find set into its lowest-signature cluster (others → ``merged_from``)."""
    ordered = sorted(range(len(clusters)), key=lambda i: clusters[i].signature)
    roots: dict[int, Cluster] = {}
    for i in ordered:
        root = uf.find(i)
        cluster = clusters[i]
        if root not in roots:
            roots[root] = cluster
            continue
        keeper = roots[root]
        keeper.members.extend(cluster.members)
        keeper.merged_from.append(cluster.signature)
        keeper.merged_from.extend(cluster.merged_from)
        _take_worst_level(keeper, cluster)
    return [_sorted(c) for c in roots.values()]


def _take_worst_level(keeper: Cluster, other: Cluster) -> None:
    """A merged log cluster keeps the most severe level and the first non-empty exception type."""
    if keeper.kind != "log":
        return
    if other.level and (
        keeper.level is None or LEVEL_ORDER.index(other.level) < LEVEL_ORDER.index(keeper.level)
    ):
        keeper.level = other.level
    if not keeper.exception_type:
        keeper.exception_type = other.exception_type


def merge_log_clusters(
    clusters: list[Cluster],
    variant: Variant,
    provider: LLMProvider | None = None,
    frame_tfidf_threshold: float = 0.5,
    frame_embed_threshold: float = 0.80,
    tfidf_only_threshold: float = 0.8,
) -> list[Cluster]:
    """Spec §5 variants on top of exact grouping. Detector clusters pass through untouched."""
    if variant not in VARIANTS:
        raise ValueError(f"unknown cluster variant {variant!r}; use one of {', '.join(VARIANTS)}")
    if variant == "exact":
        return list(clusters)
    logs = sorted((c for c in clusters if c.kind == "log"), key=lambda c: c.signature)
    others = [c for c in clusters if c.kind != "log"]
    if len(logs) < 2:
        return list(clusters)
    texts = [c.normalized_message for c in logs]
    if variant == "frame_embed":
        if provider is None:
            raise ValueError("frame_embed needs an embedding provider")
        matrix = provider.embed(texts)
        threshold = frame_embed_threshold
    else:
        # token_pattern keeps 1-char tokens and placeholders like <n>, so an all-placeholder
        # corpus can't raise "empty vocabulary".
        matrix = TfidfVectorizer(token_pattern=r"(?u)\S+").fit_transform(texts).toarray()
        threshold = frame_tfidf_threshold if variant == "frame_tfidf" else tfidf_only_threshold
    similarity = matrix @ matrix.T  # rows are L2-normalized, so this is cosine
    uf = UnionFind(len(logs))
    for i in range(len(logs)):
        for j in range(i + 1, len(logs)):
            if similarity[i, j] < threshold:
                continue
            if variant != "tfidf_only" and not _same_top_frame(logs[i], logs[j]):
                continue
            uf.union(i, j)
    return sorted(_apply_union(logs, uf) + others, key=lambda c: c.signature)


def _same_top_frame(a: Cluster, b: Cluster) -> bool:
    fa, fb = a.top_frame, b.top_frame
    return fa is not None and fb is not None and fa.qualified == fb.qualified


def build_clusters(
    runs: Mapping[str, LoadedRun],
    variant: Variant = "frame_tfidf",
    provider: LLMProvider | None = None,
    min_level: str = "warning",
    cell_size_m: float = 4.0,
    dbscan_eps_m: float = 3.0,
    merge_thresholds: Mapping[str, float] | None = None,
) -> list[Cluster]:
    """Exact grouping, then the requested merge variant. Output sorted by signature."""
    thresholds = dict(merge_thresholds or {})
    exact = exact_clusters(runs, min_level, cell_size_m, dbscan_eps_m)
    return merge_log_clusters(exact, variant, provider, **thresholds)
