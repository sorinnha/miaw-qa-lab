"""A small benchmark folder for eval tests: copies of samples/sample_run with their own run ids."""

from __future__ import annotations

import json
import shutil
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
SAMPLE_RUN = REPO / "samples" / "sample_run"
DESIGN_DOC = REPO / "docs" / "sandbox_design.md"


def copy_run(target_root: Path, run_id: str, labels: bool = True) -> Path:
    """Copy the sample run to ``target_root/run_id`` with every ``run_id`` field rewritten."""
    target = target_root / run_id
    shutil.copytree(SAMPLE_RUN, target, ignore=shutil.ignore_patterns("EXPECTED.md"))
    events = [
        json.loads(line) for line in (target / "events.jsonl").read_text("utf-8").splitlines()
    ]
    for event in events:
        event["run_id"] = run_id
    (target / "events.jsonl").write_text(
        "".join(json.dumps(e, separators=(",", ":")) + "\n" for e in events), "utf-8"
    )
    for name in ("run.json", "labels.json"):
        data = json.loads((target / name).read_text("utf-8"))
        data["run_id"] = run_id
        (target / name).write_text(json.dumps(data, indent=2) + "\n", "utf-8")
    if not labels:
        (target / "labels.json").unlink()
    return target


def make_benchmark(root: Path, runs: int = 2, unlabelled: int = 0) -> Path:
    root.mkdir(parents=True, exist_ok=True)
    for i in range(runs):
        copy_run(root, f"20261005T1030{i:02d}Z-s{i + 1}")
    for i in range(unlabelled):
        copy_run(root, f"20261005T1100{i:02d}Z-s{i + 50}", labels=False)
    return root
