"""Write the files in ``<out>/`` (spec 02 §11). JSON is UTF-8, LF, indented, keys as produced."""

from __future__ import annotations

import json
import shutil
from collections.abc import Iterable, Mapping
from pathlib import Path
from typing import Any

from qalab.io.runs import LoadedRun
from qalab.models.bug import BugReport
from qalab.triage.cluster import Cluster


def write_json(path: Path, data: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, indent=2, ensure_ascii=False)
        f.write("\n")


def write_text(path: Path, text: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8", newline="\n")


def write_bugs_json(path: Path, reports: Iterable[BugReport]) -> None:
    write_json(path, [r.to_json_dict() for r in reports])


def cluster_to_dict(cluster: Cluster) -> dict[str, Any]:
    """Debug view for ``clusters.json``."""
    return {
        "signature": cluster.signature,
        "kind": cluster.kind,
        "label": cluster.label,
        "level": cluster.level,
        "exception_type": cluster.exception_type or None,
        "normalized_message": cluster.normalized_message or None,
        "detector": cluster.detector,
        "detector_severity": cluster.detector_severity,
        "cell": list(cluster.cell) if cluster.cell else None,
        "top_frames": cluster.top_frames,
        "count": cluster.count,
        "runs": cluster.run_ids,
        "scenes": cluster.scenes,
        "first_t": cluster.first_t,
        "last_t": cluster.last_t,
        "priority": cluster.priority,
        "score": cluster.score,
        "score_breakdown": cluster.score_breakdown,
        "merged_from": cluster.merged_from,
        "members": [{"run_id": m.event.run_id, "seq": m.event.seq} for m in cluster.members],
    }


def write_clusters_json(path: Path, clusters: Iterable[Cluster]) -> None:
    write_json(path, [cluster_to_dict(c) for c in clusters])


def copy_screenshot(run: LoadedRun, relative: str, out_dir: Path) -> str | None:
    """Copy ``<run>/shots/x.png`` to ``<out>/shots/<run_id>/x.png``; return the new path."""
    source = run.run_dir / relative
    if not source.is_file():
        return None
    target_rel = Path("shots") / run.run.run_id / Path(relative).name
    target = out_dir / target_rel
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(source, target)
    return target_rel.as_posix()


def copy_attachments(
    report: BugReport, cluster: Cluster, runs: Mapping[str, LoadedRun], out_dir: Path
) -> BugReport:
    """Rewrite ``attachments`` to paths relative to the reports folder (files copied there)."""
    copied: list[str] = []
    for member in cluster.members:
        shot = (member.event.data or {}).get("screenshot")
        if shot and member.event.run_id in runs:
            new_path = copy_screenshot(runs[member.event.run_id], shot, out_dir)
            if new_path and new_path not in copied:
                copied.append(new_path)
    return report.model_copy(update={"attachments": copied or None})
