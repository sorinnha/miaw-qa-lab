"""Spec 02 §6: explainable ranking. No ML, every factor shows up in ``score_breakdown``."""

from __future__ import annotations

import math
from collections.abc import Mapping

from qalab.config import RankConfig
from qalab.io.runs import LoadedRun
from qalab.models.bug import Priority
from qalab.triage.cluster import Cluster

W_LOG = {"exception": 5.0, "assert": 4.0, "error": 3.0, "warning": 1.0}
W_DET = {"blocker": 6.0, "critical": 5.0, "major": 4.0, "minor": 2.0, "trivial": 1.0}


def base_weight(cluster: Cluster) -> float:
    if cluster.kind == "log":
        return W_LOG.get(cluster.level or "", 1.0)
    return W_DET.get(cluster.detector_severity or "", 1.0)


def score_cluster(
    cluster: Cluster, runs: Mapping[str, LoadedRun], config: RankConfig | None = None
) -> Cluster:
    """Fill ``score``, ``priority`` and ``score_breakdown`` in place and return the cluster."""
    config = config or RankConfig()
    w = base_weight(cluster)
    count_factor = 1.0 + math.log2(cluster.count)
    runs_factor = 1.0 + 0.5 * (cluster.runs_affected - 1)
    crash = any(runs[r].suspected_crash for r in cluster.run_ids if r in runs)
    crash_factor = config.crash_multiplier if crash else 1.0
    score = w * count_factor * runs_factor * crash_factor
    cluster.score = round(score, 4)
    cluster.priority = priority_for(score, cluster, config)
    cluster.score_breakdown = {
        "weight": w,
        "count": float(cluster.count),
        "count_factor": round(count_factor, 4),
        "runs_affected": float(cluster.runs_affected),
        "runs_factor": runs_factor,
        "crash_factor": crash_factor,
        "score": cluster.score,
    }
    return cluster


def priority_for(score: float, cluster: Cluster, config: RankConfig) -> Priority:
    if score >= config.p1 or cluster.detector_severity == "blocker":
        return "P1"
    if score >= config.p2:
        return "P2"
    if score >= config.p3:
        return "P3"
    return "P4"


def rank_clusters(
    clusters: list[Cluster], runs: Mapping[str, LoadedRun], config: RankConfig | None = None
) -> list[Cluster]:
    """Score every cluster; best first (priority, then score, then signature for stable ties)."""
    for cluster in clusters:
        score_cluster(cluster, runs, config)
    return sorted(clusters, key=lambda c: (c.priority, -c.score, c.signature))
