"""Hand-made clusters from samples/sample_run, standing in for M2's cluster step."""

from __future__ import annotations

import hashlib
import re
from pathlib import Path

from qalab.io.runs import LoadedRun, load_run
from qalab.models.bug import BugKind
from qalab.triage.cluster import Cluster, ClusterMember

REPO = Path(__file__).resolve().parents[3]
SAMPLE_RUN = REPO / "samples" / "sample_run"

# "KeyNotFoundException: The given key" → "KeyNotFoundException"
_EXCEPTION = re.compile(r"^(\w+(?:Exception|Error))\b")


def load_sample() -> LoadedRun:
    return load_run(SAMPLE_RUN)


def make_cluster(loaded: LoadedRun, seqs: list[int], **overrides) -> Cluster:
    """Build a cluster from the given seqs; kind/level/detector are read off the first event."""
    by_seq = {e.seq: e for e in loaded.events}
    members = [ClusterMember.from_event(by_seq[s]) for s in seqs]
    first = members[0].event
    kind: BugKind = "log" if first.kind == "log" else "detector"
    fields: dict = {
        "signature": hashlib.sha1(f"fixture|{seqs}".encode()).hexdigest()[:12],
        "kind": kind,
        "members": members,
    }
    if kind == "log":
        match = _EXCEPTION.match(first.message or "")
        fields.update(
            level=first.level,
            exception_type=match.group(1) if match else "",
            normalized_message=first.message or "",
        )
    else:
        data = first.detector()
        fields.update(detector=data.detector, detector_severity=data.severity)
    fields.update(overrides)
    return Cluster(**fields)


# seqs per seeded bug in the sample run (see samples/sample_run/EXPECTED.md)
SB01_DOORS = [8]
SB02_INVENTORY = [11, 15]
SB03_ENEMY_REGISTRY = [19, 26]
SB04_SPAWNER = [18]
SB06_FELL = [23]
SB08_PERF = [21]
SB13_AUDIO = [13, 16, 28]
SB14_COMBAT = [30, 31]
