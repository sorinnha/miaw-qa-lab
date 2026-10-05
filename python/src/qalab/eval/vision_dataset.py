"""``qalab vision dataset``: screenshots + their ``labels.json`` labels → a split image dataset.

Ground-truth tooling (it reads ``labels.json``), so it lives in ``qalab.eval``. Splits are by run,
never by frame: frames from one run are near-duplicates, and a frame split would leak test frames
into training and inflate every score (spec 03).
"""

from __future__ import annotations

import csv
import json
import random
import shutil
from collections import Counter
from collections.abc import Sequence
from dataclasses import dataclass
from pathlib import Path

from qalab.eval.ground_truth import load_labels
from qalab.triage.pipeline import load_runs
from qalab.vision.analyze import ui_event_times
from qalab.vision.hybrid import event_gaps

INDEX = "index.csv"
STATS = "stats.json"
COLUMNS = ("path", "run_id", "t", "scene", "labels", "split", "event_gap_s", "method")
SPLITS = ("train", "val", "test")
LABELS = ("missing_texture", "black_screen", "ui_overflow", "placeholder_ui")
FEW_TEST_FRAMES = 10


@dataclass
class Frame:
    path: str  # relative to the dataset folder
    run_id: str
    t: float | None
    scene: str
    labels: list[str]
    split: str
    event_gap_s: float | None  # seconds to the nearest UI action or detector event (hybrid policy)
    method: str  # capture method: screen_capture | camera_render

    def row(self) -> dict[str, str]:
        return {
            "path": self.path,
            "run_id": self.run_id,
            "t": "" if self.t is None else f"{self.t:g}",
            "scene": self.scene,
            "labels": "|".join(self.labels),
            "split": self.split,
            "event_gap_s": "" if self.event_gap_s is None else f"{self.event_gap_s:g}",
            "method": self.method,
        }

    @staticmethod
    def from_row(row: dict[str, str]) -> Frame:
        return Frame(
            path=row["path"],
            run_id=row["run_id"],
            t=float(row["t"]) if row.get("t") else None,
            scene=row.get("scene", ""),
            labels=[x for x in row.get("labels", "").split("|") if x],
            split=row["split"],
            event_gap_s=float(row["event_gap_s"]) if row.get("event_gap_s") else None,
            method=row.get("method", ""),
        )


def split_runs(
    run_ids: Sequence[str], seed: int = 7, fractions: tuple[float, float, float] = (0.7, 0.15, 0.15)
) -> dict[str, str]:
    """Seeded split of run ids into train/val/test (70/15/15). With 3+ runs, val and test get at
    least one run each. The same ids and seed always give the same split."""
    ids = sorted(set(run_ids))
    random.Random(seed).shuffle(ids)
    n = len(ids)
    n_val = round(n * fractions[1])
    n_test = round(n * fractions[2])
    if n >= 3:
        n_val, n_test = max(1, n_val), max(1, n_test)
    n_train = max(0, n - n_val - n_test)
    split: dict[str, str] = {}
    for i, run_id in enumerate(ids):
        split[run_id] = "train" if i < n_train else ("val" if i < n_train + n_val else "test")
    return split


def build_dataset(run_dirs: Sequence[str | Path], out: Path, seed: int = 7) -> list[Frame]:
    """Copy every labelled screenshot into ``out/images/<run_id>/`` and write ``index.csv`` and
    ``stats.json``. Runs without ``labels.json`` are skipped (no ground truth)."""
    runs = load_runs(run_dirs)
    labelled = {rid: (r, load_labels(r.run_dir)) for rid, r in runs.items()}
    labelled = {rid: v for rid, v in labelled.items() if v[1] is not None}
    if not labelled:
        raise ValueError("no benchmark runs (with labels.json) among the given folders")
    splits = split_runs(list(labelled), seed)
    out.mkdir(parents=True, exist_ok=True)
    frames: list[Frame] = []
    for run_id, (loaded, labels) in sorted(labelled.items()):
        truth = {shot.path: shot for shot in labels.screenshots}  # type: ignore[union-attr]
        shots = [
            e
            for e in loaded.events
            if e.kind == "screenshot" and e.data and e.data["path"] in truth
        ]
        gaps = event_gaps([e.t for e in shots], ui_event_times(loaded.events))
        for event, gap in zip(shots, gaps, strict=True):
            source = loaded.run_dir / event.data["path"]  # type: ignore[index]
            if not source.is_file():
                continue
            relative = Path("images") / run_id / Path(event.data["path"]).name  # type: ignore[index]
            (out / relative).parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, out / relative)
            frames.append(
                Frame(
                    path=relative.as_posix(),
                    run_id=run_id,
                    t=event.t,
                    scene=event.scene or "",
                    labels=sorted(truth[event.data["path"]].labels),  # type: ignore[index]
                    split=splits[run_id],
                    event_gap_s=gap,
                    method=str(event.data.get("method") or ""),  # type: ignore[union-attr]
                )
            )
    with (out / INDEX).open("w", encoding="utf-8", newline="") as f:
        writer = csv.DictWriter(f, fieldnames=COLUMNS, lineterminator="\n")
        writer.writeheader()
        writer.writerows(frame.row() for frame in frames)
    (out / STATS).write_text(
        json.dumps(dataset_stats(frames, seed), indent=2) + "\n", encoding="utf-8"
    )
    return frames


def load_dataset(root: Path) -> list[Frame]:
    with (Path(root) / INDEX).open(encoding="utf-8", newline="") as f:
        return [Frame.from_row(row) for row in csv.DictReader(f)]


def dataset_stats(frames: Sequence[Frame], seed: int | None = None) -> dict[str, object]:
    """Frames, runs and positives per label for every split, plus warnings for thin test classes."""
    stats: dict[str, object] = {"seed": seed, "frames": len(frames), "splits": {}}
    warnings: list[str] = []
    for split in sorted({f.split for f in frames}):
        part = [f for f in frames if f.split == split]
        counts = Counter(label for f in part for label in f.labels)
        stats["splits"][split] = {  # type: ignore[index]
            "frames": len(part),
            "runs": len({f.run_id for f in part}),
            "clean": sum(1 for f in part if not f.labels),
            "labels": {label: counts.get(label, 0) for label in LABELS},
        }
        if split == "test":
            warnings += [
                f"test has only {counts.get(label, 0)} '{label}' frames "
                f"(< {FEW_TEST_FRAMES}): its scores are noisy"
                for label in LABELS
                if counts.get(label, 0) < FEW_TEST_FRAMES
            ]
    stats["warnings"] = warnings
    return stats
