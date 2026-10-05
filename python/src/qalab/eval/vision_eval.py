"""``qalab eval vision``: which screenshot method works, measured on the split dataset (spec 03).

Discipline: heuristic thresholds and the ML model's thresholds are picked on **val**, the hybrid's
N and window are picked on **val**, and every reported number is on **test** (plus the hand-labelled
``real`` split when the dataset has one). Definitions are in DECISIONS D-029.
"""

from __future__ import annotations

import json
import logging
import re
import shutil
import time
from collections.abc import Callable, Sequence
from dataclasses import asdict, dataclass, field, replace
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

import numpy as np

from qalab import __version__
from qalab.eval.metrics import percentile
from qalab.eval.vision_dataset import LABELS, Frame, load_dataset
from qalab.llm.base import LLMProvider
from qalab.vision import heuristics, vlm
from qalab.vision.hybrid import HybridPolicy
from qalab.vision.images import load_rgb
from qalab.vision.ml import MlModel, features, train

log = logging.getLogger(__name__)

METHODS = ("heuristic", "vlm", "ml", "hybrid")
BLACK_GRID = (0.5, 0.6, 0.7, 0.8, 0.85, 0.9, 0.93, 0.95, 0.97, 0.99)
MAGENTA_GRID = (0.0005, 0.001, 0.002, 0.003, 0.005, 0.01, 0.02, 0.05)
HYBRID_N_GRID = (1, 2, 3, 5, 8, 13)
HYBRID_WINDOW_GRID = (0.0, 1.0, 2.0, 4.0)
H3_RECALL_SHARE = 0.9  # hypothesis H3: keep ≥ 90% of the VLM's macro recall


@dataclass
class LabelScore:
    precision: float | None
    recall: float | None
    f1: float | None
    tp: int
    fp: int
    fn: int
    support: int


@dataclass
class MethodScore:
    method: str
    split: str
    frames: int
    per_label: dict[str, LabelScore]
    macro_f1: float | None  # over labels with at least one positive frame in this split
    macro_recall: float | None
    fp_per_100: float | None
    vlm_calls: int = 0
    latency_p50_ms: float | None = None
    latency_p95_ms: float | None = None
    est_cost: float | None = None  # vlm_calls × cost_per_1k_images / 1000, when configured
    settings: dict[str, Any] = field(default_factory=dict)


def score_predictions(
    method: str, split: str, truth: Sequence[set[str]], predicted: Sequence[set[str]]
) -> MethodScore:
    """Multi-label scores: a frame can carry several labels, each counted on its own."""
    if len(truth) != len(predicted):
        raise ValueError("truth and predictions differ in length")
    per_label: dict[str, LabelScore] = {}
    total_fp = 0
    for label in LABELS:
        tp = sum(1 for t, p in zip(truth, predicted, strict=True) if label in t and label in p)
        fp = sum(1 for t, p in zip(truth, predicted, strict=True) if label not in t and label in p)
        fn = sum(1 for t, p in zip(truth, predicted, strict=True) if label in t and label not in p)
        total_fp += fp
        precision = tp / (tp + fp) if tp + fp else None
        recall = tp / (tp + fn) if tp + fn else None
        f1 = 2 * tp / (2 * tp + fp + fn) if tp + fp + fn else None
        per_label[label] = LabelScore(
            _r(precision), _r(recall), _r(f1), tp, fp, fn, support=tp + fn
        )
    measured = [s for s in per_label.values() if s.support > 0]
    macro_f1 = _r(float(np.mean([s.f1 or 0.0 for s in measured]))) if measured else None
    macro_recall = _r(float(np.mean([s.recall or 0.0 for s in measured]))) if measured else None
    fp_per_100 = _r(100.0 * total_fp / len(truth)) if truth else None
    return MethodScore(method, split, len(truth), per_label, macro_f1, macro_recall, fp_per_100)


def _r(value: float | None) -> float | None:
    return round(value, 4) if value is not None else None


def _labels(found: Sequence[Any]) -> set[str]:
    return {f.label for f in found if f.score >= 0.5}


# ---- heuristics ----------------------------------------------------------------------------------


def tune_thresholds(
    stats: Sequence[heuristics.Stats], truth: Sequence[set[str]]
) -> tuple[heuristics.Thresholds, dict[str, str]]:
    """Pick the black and magenta thresholds with the best F1 on these (val) frames. A label with
    no positive frame keeps the spec's starting value (the note says so)."""
    base = heuristics.Thresholds()
    notes: dict[str, str] = {}

    def best(
        label: str, attr: str, grid: Sequence[float], stat: Callable[[heuristics.Stats], float]
    ) -> float:
        default = getattr(base, attr)
        if not any(label in t for t in truth):
            notes[label] = f"no {label} frames in val: kept {default}"
            return default

        def f1(threshold: float) -> float:
            score = score_predictions(
                "tune", "val", truth, [{label} if stat(s) >= threshold else set() for s in stats]
            ).per_label[label]
            return score.f1 or 0.0

        return max(grid, key=lambda th: (f1(th), -abs(th - default)))

    black = best("black_screen", "black_ratio", BLACK_GRID, lambda s: s.black_ratio)
    magenta = best("missing_texture", "magenta_ratio", MAGENTA_GRID, lambda s: s.magenta_ratio)
    return replace(base, black_ratio=black, magenta_ratio=magenta), notes


# ---- hybrid --------------------------------------------------------------------------------------


@dataclass
class HybridPoint:
    every_n: int
    window_s: float
    vlm_calls: int
    macro_recall: float | None
    macro_f1: float | None


def simulate_hybrid(
    frames: Sequence[Frame],
    heuristic: Sequence[set[str]],
    vlm_labels: Sequence[set[str]],
    every_n: int,
    window_s: float,
) -> tuple[list[set[str]], int]:
    """What the hybrid would have predicted, from per-frame heuristic and VLM answers computed once
    (frames in run and time order; one policy per run, as ``qalab vision analyze`` does)."""
    predicted: list[set[str]] = []
    calls = 0
    policy: HybridPolicy | None = None
    current_run: str | None = None
    for frame, h, v in zip(frames, heuristic, vlm_labels, strict=True):
        if frame.run_id != current_run:
            policy, current_run = HybridPolicy(every_n, window_s), frame.run_id
        if policy.should_call_vlm(bool(h), frame.event_gap_s):  # type: ignore[union-attr]
            calls += 1
            predicted.append(h | v)
        else:
            predicted.append(set(h))
    return predicted, calls


def tune_hybrid(
    frames: Sequence[Frame],
    truth: Sequence[set[str]],
    heuristic: Sequence[set[str]],
    vlm_labels: Sequence[set[str]],
) -> tuple[int, float, list[HybridPoint]]:
    """The cheapest (N, window) on val that keeps ≥ 90% of the VLM-only macro recall (H3);
    if none does, the one with the best recall. Returns the choice and every point tried."""
    target = score_predictions("vlm", "val", truth, vlm_labels).macro_recall
    points: list[HybridPoint] = []
    for n in HYBRID_N_GRID:
        for window in HYBRID_WINDOW_GRID:
            predicted, calls = simulate_hybrid(frames, heuristic, vlm_labels, n, window)
            score = score_predictions("hybrid", "val", truth, predicted)
            points.append(HybridPoint(n, window, calls, score.macro_recall, score.macro_f1))
    good = [
        p for p in points if target is None or (p.macro_recall or 0.0) >= H3_RECALL_SHARE * target
    ]
    if good:
        choice = min(good, key=lambda p: (p.vlm_calls, -(p.macro_recall or 0.0), p.every_n))
    else:
        choice = max(points, key=lambda p: ((p.macro_recall or 0.0), -p.vlm_calls))
    return choice.every_n, choice.window_s, points


# ---- the command -------------------------------------------------------------------------------


@dataclass
class VisionEvalResult:
    dataset: str
    created_at: str
    qalab_version: str
    frames: dict[str, int]
    scores: list[MethodScore] = field(default_factory=list)
    thresholds: dict[str, Any] = field(default_factory=dict)
    tuning_notes: dict[str, str] = field(default_factory=dict)
    hybrid_val: list[HybridPoint] = field(default_factory=list)
    hybrid_test_curve: list[HybridPoint] = field(default_factory=list)
    skipped: dict[str, str] = field(default_factory=dict)
    errors: list[dict[str, str]] = field(default_factory=list)
    ml_overfit: dict[str, float | None] = field(default_factory=dict)  # macro F1 on train vs test

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


@dataclass
class VisionEvalOptions:
    methods: Sequence[str] = ("heuristic", "vlm", "hybrid")
    provider: LLMProvider | None = None
    cost_per_1k_images: float | None = None
    max_retries: int = 2
    seed: int = 7


def evaluate_vision(dataset_dir: Path, options: VisionEvalOptions) -> VisionEvalResult:
    dataset_dir = Path(dataset_dir)
    frames = sorted(load_dataset(dataset_dir), key=lambda f: (f.run_id, f.t or 0.0, f.path))
    by_split = {s: [f for f in frames if f.split == s] for s in ("train", "val", "test", "real")}
    if not by_split["test"]:
        raise ValueError(f"{dataset_dir}: the dataset has no test frames")
    result = VisionEvalResult(
        dataset=str(dataset_dir),
        created_at=datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ"),
        qalab_version=__version__,
        frames={s: len(v) for s, v in by_split.items() if v},
    )
    images: dict[str, np.ndarray] = {}

    def image(frame: Frame) -> np.ndarray:
        if frame.path not in images:
            images[frame.path] = load_rgb(dataset_dir / frame.path)
        return images[frame.path]

    eval_splits = [s for s in ("test", "real") if by_split[s]]
    truth = {s: [set(f.labels) for f in by_split[s]] for s in by_split}

    # Heuristics: statistics once per frame, thresholds tuned on val.
    stats: dict[str, list[heuristics.Stats]] = {}
    heuristic_ms: list[float] = []
    for split in ("val", *eval_splits):
        stats[split] = []
        for frame in by_split[split]:
            started = time.perf_counter()
            stats[split].append(heuristics.frame_stats(image(frame)))
            heuristic_ms.append((time.perf_counter() - started) * 1000)
    thresholds, notes = (
        tune_thresholds(stats["val"], truth["val"])
        if by_split["val"]
        else (heuristics.Thresholds(), {"all": "no val split: spec thresholds kept"})
    )
    result.thresholds = thresholds.to_dict()
    result.tuning_notes = notes
    heuristic_labels = {
        s: [_labels(heuristics.labels_from_stats(st, thresholds)) for st in stats[s]] for s in stats
    }
    if "heuristic" in options.methods:
        for split in eval_splits:
            score = score_predictions("heuristic", split, truth[split], heuristic_labels[split])
            score.latency_p50_ms = _r(percentile(heuristic_ms, 50))
            score.latency_p95_ms = _r(percentile(heuristic_ms, 95))
            score.settings = {"thresholds": result.thresholds}
            result.scores.append(score)

    # VLM: one answer per val and test frame (the hybrid is simulated from these answers).
    vlm_labels: dict[str, list[set[str]]] = {}
    if "vlm" in options.methods or "hybrid" in options.methods:
        if options.provider is None:
            for method in ("vlm", "hybrid"):
                if method in options.methods:
                    result.skipped[method] = "needs a vision provider (--provider none has none)"
        else:
            vlm_ms: dict[str, list[float]] = {}
            for split in ("val", *eval_splits):
                vlm_labels[split], vlm_ms[split] = [], []
                for frame in by_split[split]:
                    verdict = vlm.analyze(
                        image(frame),
                        options.provider,
                        frame.path,
                        frame.scene,
                        frame.t,
                        options.max_retries,
                    )
                    vlm_labels[split].append(_labels(verdict.labels))
                    if not verdict.cached:
                        vlm_ms[split].append(verdict.latency_ms)
            if "vlm" in options.methods:
                for split in eval_splits:
                    score = score_predictions("vlm", split, truth[split], vlm_labels[split])
                    score.vlm_calls = len(by_split[split])
                    score.latency_p50_ms = _r(percentile(vlm_ms[split], 50))
                    score.latency_p95_ms = _r(percentile(vlm_ms[split], 95))
                    score.est_cost = _cost(score.vlm_calls, options.cost_per_1k_images)
                    score.settings = {"model": options.provider.model}
                    result.scores.append(score)
            if "hybrid" in options.methods:
                if by_split["val"]:
                    n, window, result.hybrid_val = tune_hybrid(
                        by_split["val"], truth["val"], heuristic_labels["val"], vlm_labels["val"]
                    )
                else:
                    n, window = 5, 2.0
                for split in eval_splits:
                    predicted, calls = simulate_hybrid(
                        by_split[split], heuristic_labels[split], vlm_labels[split], n, window
                    )
                    score = score_predictions("hybrid", split, truth[split], predicted)
                    score.vlm_calls = calls
                    score.est_cost = _cost(calls, options.cost_per_1k_images)
                    score.settings = {
                        "every_n": n,
                        "window_s": window,
                        "thresholds": result.thresholds,
                    }
                    result.scores.append(score)
                for every_n in HYBRID_N_GRID:
                    predicted, calls = simulate_hybrid(
                        by_split["test"],
                        heuristic_labels["test"],
                        vlm_labels["test"],
                        every_n,
                        window,
                    )
                    s = score_predictions("hybrid", "test", truth["test"], predicted)
                    result.hybrid_test_curve.append(
                        HybridPoint(every_n, window, calls, s.macro_recall, s.macro_f1)
                    )

    # Optional ML baseline: train on train, thresholds on val, scores on test.
    if "ml" in options.methods:
        if not by_split["train"]:
            result.skipped["ml"] = "needs a train split"
        else:
            x = {
                s: np.array([features(image(f)) for f in by_split[s]])
                for s in ("train", "val", *eval_splits)
                if by_split[s]
            }
            model = train(
                x["train"],
                truth["train"],
                x.get("val", x["train"]),
                truth["val"] or truth["train"],
                options.seed,
            )
            train_pred = _ml_labels(model, x["train"])
            result.ml_overfit["train_macro_f1"] = score_predictions(
                "ml", "train", truth["train"], train_pred
            ).macro_f1
            for split in eval_splits:
                started = time.perf_counter()
                predicted = _ml_labels(model, x[split])
                per_frame = (time.perf_counter() - started) * 1000 / max(1, len(predicted))
                score = score_predictions("ml", split, truth[split], predicted)
                score.latency_p50_ms = score.latency_p95_ms = _r(per_frame)
                score.settings = {"thresholds": model.thresholds}
                result.scores.append(score)
                if split == "test":
                    result.ml_overfit["test_macro_f1"] = score.macro_f1

    result.errors = _error_examples(
        result,
        by_split["test"],
        truth["test"],
        heuristic_labels.get("test", []),
        vlm_labels.get("test"),
    )
    return result


def _ml_labels(model: MlModel, x: np.ndarray) -> list[set[str]]:
    probs = model.probabilities(x)
    return [
        {label for label in LABELS if probs[label][i] >= model.thresholds.get(label, 0.5)}
        for i in range(len(x))
    ]


def _cost(calls: int, per_1k: float | None) -> float | None:
    return round(calls * per_1k / 1000.0, 4) if per_1k is not None else None


def _error_examples(
    result: VisionEvalResult,
    frames: Sequence[Frame],
    truth: Sequence[set[str]],
    heuristic: Sequence[set[str]],
    vlm_labels: Sequence[set[str]] | None,
) -> list[dict[str, str]]:
    """Up to 3 false positives and 3 misses of the method the write-up leads with (the hybrid when
    it ran, else the heuristics)."""
    method = (
        "hybrid" if any(s.method == "hybrid" for s in result.scores) and vlm_labels else "heuristic"
    )
    if method == "hybrid":
        hybrid = next(s for s in result.scores if s.method == "hybrid" and s.split == "test")
        predicted, _ = simulate_hybrid(
            frames,
            heuristic,
            vlm_labels or [],
            hybrid.settings["every_n"],
            hybrid.settings["window_s"],
        )
    else:
        predicted = list(heuristic)
    if not predicted:
        return []
    false_positives, misses = [], []
    for frame, t, p in zip(frames, truth, predicted, strict=True):
        for label in sorted(p - t):
            false_positives.append(
                {
                    "kind": "false_positive",
                    "method": method,
                    "path": frame.path,
                    "label": label,
                    "truth": "|".join(sorted(t)) or "clean",
                }
            )
        for label in sorted(t - p):
            misses.append(
                {
                    "kind": "miss",
                    "method": method,
                    "path": frame.path,
                    "label": label,
                    "predicted": "|".join(sorted(p)) or "nothing",
                }
            )
    return false_positives[:3] + misses[:3]


def _fmt(value: float | None) -> str:
    return "n/a" if value is None else f"{value:.2f}"


def to_markdown(result: VisionEvalResult) -> str:
    """The EVAL_RESULTS.md vision table, filled from this result."""
    lines = [
        f"Dataset `{result.dataset}`: "
        + ", ".join(f"{s} {n}" for s, n in result.frames.items())
        + f" frames. Created {result.created_at}, qalab {result.qalab_version}.",
        "",
        "| Method | Split | missing_texture P/R | black_screen P/R | ui_overflow P/R "
        "| placeholder_ui P/R | Macro F1 | FP/100 frames | VLM calls | p95 latency ms "
        "| Est. cost |",
        "|---|---|---|---|---|---|---|---|---|---|---|",
    ]
    for s in result.scores:
        cells = [
            f"{_fmt(s.per_label[label].precision)}/{_fmt(s.per_label[label].recall)}"
            for label in LABELS
        ]
        lines.append(
            f"| {s.method} | {s.split} | "
            + " | ".join(cells)
            + f" | {_fmt(s.macro_f1)} | {_fmt(s.fp_per_100)} | {s.vlm_calls} "
            + f"| {_fmt(s.latency_p95_ms)} | "
            + ("n/a" if s.est_cost is None else f"{s.est_cost:.4f}")
            + " |"
        )
    for method, reason in result.skipped.items():
        lines.append(f"| {method} | skipped: {reason} | | | | | | | | | |")
    lines.append("")
    lines.append(
        f"Heuristic thresholds (tuned on val): black ≥ {result.thresholds.get('black_ratio')}, "
        f"magenta ≥ {result.thresholds.get('magenta_ratio')}."
    )
    for note in result.tuning_notes.values():
        lines.append(f"- {note}")
    hybrid = next((s for s in result.scores if s.method == "hybrid"), None)
    if hybrid:
        lines.append(
            f"Hybrid (tuned on val): every N = {hybrid.settings['every_n']}, "
            f"window = {hybrid.settings['window_s']} s."
        )
    if result.ml_overfit:
        lines.append(
            f"ML macro F1 train {result.ml_overfit.get('train_macro_f1')} vs test "
            f"{result.ml_overfit.get('test_macro_f1')} (a big gap = overfitting)."
        )
    if result.errors:
        lines += ["", "Error examples (thumbnails in the errors folder):"]
        for e in result.errors:
            detail = (
                f"truth {e['truth']}"
                if e["kind"] == "false_positive"
                else f"predicted {e['predicted']}"
            )
            lines.append(
                f"- {e['kind']} `{e['label']}` on `{e['path']}` ({detail}): <one-line reason>"
            )
    return "\n".join(lines)


def write_charts(result: VisionEvalResult, out: Path, stem: str) -> list[Path]:
    """P/R/F1 per label per method (grouped bars, test) and macro recall against VLM calls."""
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    written: list[Path] = []
    test = [s for s in result.scores if s.split == "test"]
    if test:
        fig, axes = plt.subplots(1, 3, figsize=(11, 3.4), sharey=True)
        width = 0.8 / max(1, len(test))
        for ax, metric in zip(axes, ("precision", "recall", "f1"), strict=True):
            for i, score in enumerate(test):
                values = [getattr(score.per_label[label], metric) or 0.0 for label in LABELS]
                ax.bar(
                    [x + (i - (len(test) - 1) / 2) * width for x in range(len(LABELS))],
                    values,
                    width,
                    label=score.method,
                )
            ax.set_xticks(
                range(len(LABELS)), [label.replace("_", "\n") for label in LABELS], fontsize=8
            )
            ax.set_title(metric.capitalize() if metric != "f1" else "F1")
            ax.set_ylim(0, 1.05)
            ax.spines[["top", "right"]].set_visible(False)
        axes[0].legend(frameon=False, fontsize=8)
        fig.tight_layout()
        path = out / f"{stem}_prf.png"
        fig.savefig(path, dpi=150)
        plt.close(fig)
        written.append(path)
    if result.hybrid_test_curve:
        fig, ax = plt.subplots(figsize=(6, 3.6))
        curve = sorted(result.hybrid_test_curve, key=lambda p: p.vlm_calls)
        ax.plot(
            [p.vlm_calls for p in curve],
            [p.macro_recall or 0.0 for p in curve],
            marker="o",
            label="hybrid (every N)",
        )
        for p in curve:
            ax.annotate(
                f"N={p.every_n}",
                (p.vlm_calls, p.macro_recall or 0.0),
                fontsize=7,
                xytext=(3, 3),
                textcoords="offset points",
            )
        for s in test:
            if s.method in ("heuristic", "vlm"):
                ax.scatter([s.vlm_calls], [s.macro_recall or 0.0], label=s.method, zorder=3)
        ax.set_xlabel("VLM calls (test)")
        ax.set_ylabel("macro recall")
        ax.set_ylim(0, 1.05)
        ax.set_title("Hybrid trade-off")
        ax.legend(frameon=False, fontsize=8)
        ax.spines[["top", "right"]].set_visible(False)
        fig.tight_layout()
        path = out / f"{stem}_hybrid_tradeoff.png"
        fig.savefig(path, dpi=150)
        plt.close(fig)
        written.append(path)
    return written


def write_result(result: VisionEvalResult, dataset_dir: Path, out: Path, label: str) -> list[Path]:
    """``vision_<label>.json``, ``.md``, the charts, and the error thumbnails, in ``out``."""
    out.mkdir(parents=True, exist_ok=True)
    stem = "vision_" + re.sub(r"[^A-Za-z0-9_.-]+", "_", label)
    errors_dir = out / f"{stem}_errors"
    if result.errors:
        errors_dir.mkdir(exist_ok=True)
        for e in result.errors:
            source = Path(dataset_dir) / e["path"]
            if source.is_file():
                shutil.copy2(
                    source,
                    errors_dir
                    / f"{e['kind']}_{e['label']}_{Path(e['path']).parent.name}_{source.name}",
                )
    json_path = out / f"{stem}.json"
    json_path.write_text(json.dumps(result.to_dict(), indent=2) + "\n", encoding="utf-8")
    md_path = out / f"{stem}.md"
    md_path.write_text(to_markdown(result) + "\n", encoding="utf-8")
    return [json_path, md_path, *write_charts(result, out, stem)]
