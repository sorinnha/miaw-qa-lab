"""``qalab eval vision``: which screenshot method works, measured on the split dataset (spec 03).

Discipline: heuristic thresholds and the ML model's thresholds are picked on **val**, the hybrid's
N and window are picked on **val**, and every reported number is on **test** (plus the hand-labelled
``real`` split when the dataset has one). Definitions are in DECISIONS D-029.
"""

from __future__ import annotations

import json
import logging
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
from qalab.eval.triage_eval import safe_label
from qalab.eval.vision_dataset import LABELS, Frame, load_dataset
from qalab.llm.base import LLMProvider
from qalab.vision import heuristics, vlm
from qalab.vision.hybrid import HybridPolicy
from qalab.vision.images import load_rgb
from qalab.vision.ml import features, train

log = logging.getLogger(__name__)

METHODS = ("heuristic", "vlm", "ml", "hybrid")
CONFIGURED = "heuristic (qalab.toml)"  # the thresholds `qalab vision analyze` really runs with
BLACK_GRID = (0.5, 0.6, 0.7, 0.8, 0.85, 0.9, 0.93, 0.95, 0.97, 0.99)
MAGENTA_GRID = (0.0005, 0.001, 0.002, 0.003, 0.005, 0.01, 0.02, 0.05)
HYBRID_N_GRID = (1, 2, 3, 5, 8, 13)
HYBRID_WINDOW_GRID = (0.0, 1.0, 2.0, 4.0)
H3_RECALL_SHARE = 0.9  # hypothesis H3: keep ≥ 90% of the VLM's macro recall
SPLITS = ("train", "val", "test", "real")


@dataclass
class LabelScore:
    precision: float | None
    recall: float | None
    f1: float | None
    tp: int
    fp: int
    fn: int
    support: int
    fp_per_100: float | None  # this label's false positives per 100 frames (H1 reads it per label)


@dataclass
class MethodScore:
    method: str
    split: str
    frames: int
    per_label: dict[str, LabelScore]
    macro_f1: float | None  # over labels with at least one positive frame in this split
    macro_recall: float | None
    fp_per_100: float | None  # all labels' false positives per 100 frames
    vlm_calls: int = 0
    latency_p50_ms: float | None = None  # per frame, everything the method does for it
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
        per_100 = 100.0 * fp / len(truth) if truth else None
        per_label[label] = LabelScore(
            _r(precision), _r(recall), _r(f1), tp, fp, fn, support=tp + fn, fp_per_100=_r(per_100)
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


def best_threshold(
    label: str,
    grid: Sequence[float],
    values: Sequence[float],
    truth: Sequence[set[str]],
    default: float,
) -> float:
    """The grid value with the best F1 for ``label`` when frames with ``value ≥ threshold`` are
    flagged; ties go to the value closest to ``default`` (the spec's starting threshold)."""

    def f1(threshold: float) -> float:
        predicted = [{label} if v >= threshold else set() for v in values]
        return score_predictions("tune", "val", truth, predicted).per_label[label].f1 or 0.0

    return max(grid, key=lambda th: (f1(th), -abs(th - default)))


def tune_thresholds(
    stats: Sequence[heuristics.Stats], truth: Sequence[set[str]]
) -> tuple[heuristics.Thresholds, dict[str, str]]:
    """Pick the black and magenta thresholds with the best F1 on these (val) frames. A label with
    no positive frame keeps the spec's starting value (the note says so)."""
    base = heuristics.Thresholds()
    notes: dict[str, str] = {}
    chosen: dict[str, float] = {}
    for label, attr, grid in (
        ("black_screen", "black_ratio", BLACK_GRID),
        ("missing_texture", "magenta_ratio", MAGENTA_GRID),
    ):
        default = getattr(base, attr)
        if not any(label in t for t in truth):
            notes[label] = f"no {label} frames in val: kept {default}"
            chosen[attr] = default
            continue
        values = [getattr(s, attr) for s in stats]
        chosen[attr] = best_threshold(label, grid, values, truth, default)
    return replace(base, **chosen), notes


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
) -> tuple[list[set[str]], list[bool]]:
    """What the hybrid would have predicted, and on which frames it would have called the VLM,
    from per-frame heuristic and VLM answers computed once (frames in run and time order; one
    policy per run, as ``qalab vision analyze`` does)."""
    predicted: list[set[str]] = []
    called: list[bool] = []
    policy = HybridPolicy(every_n, window_s)
    current_run: str | None = None
    for frame, h, v in zip(frames, heuristic, vlm_labels, strict=True):
        if frame.run_id != current_run:
            policy, current_run = HybridPolicy(every_n, window_s), frame.run_id
        call = policy.should_call_vlm(bool(h), frame.event_gap_s)
        called.append(call)
        predicted.append(h | v if call else set(h))
    return predicted, called


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
            predicted, called = simulate_hybrid(frames, heuristic, vlm_labels, n, window)
            score = score_predictions("hybrid", "val", truth, predicted)
            points.append(HybridPoint(n, window, sum(called), score.macro_recall, score.macro_f1))
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
    thresholds: dict[str, Any] = field(default_factory=dict)  # tuned on val
    tuning_notes: dict[str, str] = field(default_factory=dict)
    hybrid_val: list[HybridPoint] = field(default_factory=list)
    hybrid_test_curve: list[HybridPoint] = field(default_factory=list)
    skipped: dict[str, str] = field(default_factory=dict)
    vlm_errors: dict[str, int] = field(default_factory=dict)  # split → frames with no usable answer
    errors: list[dict[str, str]] = field(default_factory=list)  # error examples (FPs and misses)
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
    configured: heuristics.Thresholds | None = None  # qalab.toml's; scored too when they differ


class Dataset:
    """The dataset in run and time order, split into lists, with each image loaded once."""

    def __init__(self, root: Path) -> None:
        self.root = Path(root)
        frames = sorted(load_dataset(self.root), key=lambda f: (f.run_id, f.t or 0.0, f.path))
        self.by_split = {s: [f for f in frames if f.split == s] for s in SPLITS}
        self.truth = {s: [set(f.labels) for f in v] for s, v in self.by_split.items()}
        self.eval_splits = [s for s in ("test", "real") if self.by_split[s]]
        self._images: dict[str, np.ndarray] = {}

    def image(self, frame: Frame) -> np.ndarray:
        if frame.path not in self._images:
            self._images[frame.path] = load_rgb(self.root / frame.path)
        return self._images[frame.path]


@dataclass
class VlmAnswers:
    """The VLM's labels per frame, its latency (None when cached) and the frames it failed on."""

    labels: dict[str, list[set[str]]] = field(default_factory=dict)
    latency_ms: dict[str, list[float | None]] = field(default_factory=dict)
    errors: dict[str, int] = field(default_factory=dict)


def _timed(work: Callable[[], Any]) -> tuple[Any, float]:
    """Run ``work`` and return its result and how long it took in milliseconds."""
    started = time.perf_counter()
    value = work()
    return value, (time.perf_counter() - started) * 1000


def _set_latency(score: MethodScore, per_frame_ms: Sequence[float]) -> None:
    score.latency_p50_ms = _r(percentile(per_frame_ms, 50))
    score.latency_p95_ms = _r(percentile(per_frame_ms, 95))


def _cost(calls: int, per_1k: float | None) -> float | None:
    return round(calls * per_1k / 1000.0, 4) if per_1k is not None else None


def heuristic_stats(
    data: Dataset, splits: Sequence[str]
) -> tuple[dict[str, list[heuristics.Stats]], dict[str, list[float]]]:
    """Frame statistics (computed once, scored under any thresholds) and their time per frame."""
    stats: dict[str, list[heuristics.Stats]] = {}
    ms: dict[str, list[float]] = {}
    for split in splits:
        stats[split], ms[split] = [], []
        for frame in data.by_split[split]:
            frame_stats, elapsed = _timed(lambda f=frame: heuristics.frame_stats(data.image(f)))
            stats[split].append(frame_stats)
            ms[split].append(elapsed)
    return stats, ms


def heuristic_labels(
    stats: dict[str, list[heuristics.Stats]], thresholds: heuristics.Thresholds
) -> dict[str, list[set[str]]]:
    """The labels each frame gets under these thresholds."""
    return {
        split: [_labels(heuristics.labels_from_stats(s, thresholds)) for s in values]
        for split, values in stats.items()
    }


def ask_vlm(data: Dataset, splits: Sequence[str], options: VisionEvalOptions) -> VlmAnswers:
    """One VLM answer per frame of these splits; the hybrid is simulated from the same answers."""
    assert options.provider is not None
    answers = VlmAnswers()
    for split in splits:
        answers.labels[split], answers.latency_ms[split], answers.errors[split] = [], [], 0
        for frame in data.by_split[split]:
            verdict = vlm.analyze(
                data.image(frame),
                options.provider,
                frame.path,
                frame.scene,
                frame.t,
                options.max_retries,
            )
            answers.labels[split].append(_labels(verdict.labels))
            answers.latency_ms[split].append(None if verdict.cached else verdict.latency_ms)
            answers.errors[split] += verdict.error is not None
    return answers


def hybrid_latency(
    heuristic_ms: Sequence[float], vlm_ms: Sequence[float | None], called: Sequence[bool]
) -> list[float]:
    """Per-frame cost of the hybrid: the heuristics always, plus the VLM where it called it.
    Frames whose VLM answer came from the cache have no time and are left out."""
    costs: list[float] = []
    for h, v, call in zip(heuristic_ms, vlm_ms, called, strict=True):
        if not call:
            costs.append(h)
        elif v is not None:
            costs.append(h + v)
    return costs


def score_ml(
    data: Dataset, options: VisionEvalOptions, result: VisionEvalResult
) -> dict[str, list[set[str]]]:
    """Train on train, thresholds on val, score on test; the per-frame time includes features."""
    x_train = np.array([features(data.image(f)) for f in data.by_split["train"]])
    if data.by_split["val"]:
        x_val = np.array([features(data.image(f)) for f in data.by_split["val"]])
        y_val = data.truth["val"]
    else:
        x_val, y_val = x_train, data.truth["train"]
    model = train(x_train, data.truth["train"], x_val, y_val, options.seed)
    train_predicted = [_labels(model.predict(data.image(f))) for f in data.by_split["train"]]
    train_score = score_predictions("ml", "train", data.truth["train"], train_predicted)
    result.ml_overfit["train_macro_f1"] = train_score.macro_f1
    predictions: dict[str, list[set[str]]] = {}
    for split in data.eval_splits:
        predicted: list[set[str]] = []
        ms: list[float] = []
        for frame in data.by_split[split]:
            found, elapsed = _timed(lambda f=frame: model.predict(data.image(f)))
            predicted.append(_labels(found))
            ms.append(elapsed)
        score = score_predictions("ml", split, data.truth[split], predicted)
        _set_latency(score, ms)
        score.settings = {"thresholds": model.thresholds}
        result.scores.append(score)
        predictions[split] = predicted
        if split == "test":
            result.ml_overfit["test_macro_f1"] = score.macro_f1
    return predictions


def evaluate_vision(dataset_dir: Path, options: VisionEvalOptions) -> VisionEvalResult:
    """Score every requested method on test (and ``real``); see the module docstring."""
    data = Dataset(dataset_dir)
    if not data.by_split["test"]:
        raise ValueError(f"{dataset_dir}: the dataset has no test frames")
    result = VisionEvalResult(
        dataset=str(dataset_dir),
        created_at=datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ"),
        qalab_version=__version__,
        frames={s: len(v) for s, v in data.by_split.items() if v},
    )
    test_predictions: dict[str, list[set[str]]] = {}  # method → test labels, for error examples
    wants_vlm = sorted({"vlm", "hybrid"} & set(options.methods))
    heuristic: dict[str, list[set[str]]] = {}
    heuristic_ms: dict[str, list[float]] = {}

    # Heuristics (also the hybrid's first step): statistics once per frame, thresholds tuned on
    # val, scores on test.
    if {"heuristic", "hybrid"} & set(options.methods):
        stats, heuristic_ms = heuristic_stats(data, ["val", *data.eval_splits])
        if data.by_split["val"]:
            thresholds, notes = tune_thresholds(stats["val"], data.truth["val"])
        else:
            thresholds = heuristics.Thresholds()
            notes = {"all": "no val split: spec thresholds kept"}
        result.thresholds, result.tuning_notes = thresholds.to_dict(), notes
        heuristic = heuristic_labels(stats, thresholds)
    if "heuristic" in options.methods:
        _score_heuristics(data, result, heuristic, heuristic_ms, "heuristic", "tuned on val")
        test_predictions["heuristic"] = heuristic["test"]
        configured = options.configured
        if configured is not None and configured.to_dict() != result.thresholds:
            # What `qalab vision analyze` does today, until the tuned values are copied over.
            mine = heuristic_labels(stats, configured)
            _score_heuristics(data, result, mine, heuristic_ms, CONFIGURED, "qalab.toml")

    # VLM: one answer per frame (val only when the hybrid needs tuning).
    if wants_vlm and options.provider is None:
        for method in wants_vlm:
            result.skipped[method] = "needs a vision provider (--provider none has none)"
    elif wants_vlm:
        tune = ["val"] if "hybrid" in options.methods and data.by_split["val"] else []
        answers = ask_vlm(data, [*tune, *data.eval_splits], options)
        result.vlm_errors = {s: n for s, n in answers.errors.items() if n}
        for split, n in result.vlm_errors.items():
            log.warning(
                "%s: %d frame(s) got no usable VLM answer (scored as nothing found)", split, n
            )
        if "vlm" in options.methods:
            _score_vlm(data, result, answers, options)
            test_predictions["vlm"] = answers.labels["test"]
        if "hybrid" in options.methods:
            test_predictions["hybrid"] = _score_hybrid(
                data, result, heuristic, heuristic_ms, answers, options, tuned=bool(tune)
            )

    # Optional ML baseline.
    if "ml" in options.methods:
        if not data.by_split["train"]:
            result.skipped["ml"] = "needs a train split"
        else:
            test_predictions["ml"] = score_ml(data, options, result)["test"]

    result.errors = error_examples(data.by_split["test"], data.truth["test"], test_predictions)
    return result


def _score_heuristics(
    data: Dataset,
    result: VisionEvalResult,
    labels: dict[str, list[set[str]]],
    ms: dict[str, list[float]],
    method: str,
    source: str,
) -> None:
    thresholds = result.thresholds if method == "heuristic" else None
    for split in data.eval_splits:
        score = score_predictions(method, split, data.truth[split], labels[split])
        _set_latency(score, ms[split])
        score.settings = {"source": source}
        if thresholds is not None:
            score.settings["thresholds"] = thresholds
        result.scores.append(score)


def _score_vlm(
    data: Dataset, result: VisionEvalResult, answers: VlmAnswers, options: VisionEvalOptions
) -> None:
    for split in data.eval_splits:
        score = score_predictions("vlm", split, data.truth[split], answers.labels[split])
        score.vlm_calls = len(data.by_split[split])
        _set_latency(score, [ms for ms in answers.latency_ms[split] if ms is not None])
        score.est_cost = _cost(score.vlm_calls, options.cost_per_1k_images)
        score.settings = {"model": options.provider.model if options.provider else None}
        result.scores.append(score)


def _score_hybrid(
    data: Dataset,
    result: VisionEvalResult,
    heuristic: dict[str, list[set[str]]],
    heuristic_ms: dict[str, list[float]],
    answers: VlmAnswers,
    options: VisionEvalOptions,
    tuned: bool,
) -> list[set[str]]:
    """Tune N and the window on val, score on test, and trace recall against calls on test."""
    if tuned:
        n, window, result.hybrid_val = tune_hybrid(
            data.by_split["val"], data.truth["val"], heuristic["val"], answers.labels["val"]
        )
    else:
        n, window = 5, 2.0  # spec 03's defaults when there is no val split
    test_predicted: list[set[str]] = []
    for split in data.eval_splits:
        predicted, called = simulate_hybrid(
            data.by_split[split], heuristic[split], answers.labels[split], n, window
        )
        score = score_predictions("hybrid", split, data.truth[split], predicted)
        score.vlm_calls = sum(called)
        _set_latency(score, hybrid_latency(heuristic_ms[split], answers.latency_ms[split], called))
        score.est_cost = _cost(score.vlm_calls, options.cost_per_1k_images)
        score.settings = {"every_n": n, "window_s": window, "thresholds": result.thresholds}
        result.scores.append(score)
        if split == "test":
            test_predicted = predicted
    for every_n in HYBRID_N_GRID:
        predicted, called = simulate_hybrid(
            data.by_split["test"], heuristic["test"], answers.labels["test"], every_n, window
        )
        s = score_predictions("hybrid", "test", data.truth["test"], predicted)
        result.hybrid_test_curve.append(
            HybridPoint(every_n, window, sum(called), s.macro_recall, s.macro_f1)
        )
    return test_predicted


def error_examples(
    frames: Sequence[Frame], truth: Sequence[set[str]], predictions: dict[str, list[set[str]]]
) -> list[dict[str, str]]:
    """Up to 3 false positives and 3 misses on test, from the method the write-up leads with:
    the hybrid if it ran, else the VLM, the heuristics, then the ML baseline."""
    method = next((m for m in ("hybrid", "vlm", "heuristic", "ml") if m in predictions), None)
    if method is None:
        return []
    false_positives: list[dict[str, str]] = []
    misses: list[dict[str, str]] = []
    for frame, t, p in zip(frames, truth, predictions[method], strict=True):
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
    """The EVAL_RESULTS.md vision tables, filled from this result."""
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
    if result.scores:
        lines += [
            "",
            "False positives per 100 frames, per label (H1):",
            "",
            "| Method | Split | " + " | ".join(LABELS) + " |",
            "|---|---|" + "---|" * len(LABELS),
        ]
        for s in result.scores:
            cells = [_fmt(s.per_label[label].fp_per_100) for label in LABELS]
            lines.append(f"| {s.method} | {s.split} | " + " | ".join(cells) + " |")
    lines.append("")
    if result.thresholds:
        lines.append(
            f"Heuristic thresholds (tuned on val): black ≥ {result.thresholds.get('black_ratio')}, "
            f"magenta ≥ {result.thresholds.get('magenta_ratio')}. `qalab vision analyze` reads "
            "`[vision] black_ratio` and `magenta_ratio` in qalab.toml: copy these there to run "
            "what was measured."
        )
    for note in result.tuning_notes.values():
        lines.append(f"- {note}")
    for split, n in result.vlm_errors.items():
        lines.append(
            f"- Warning: {n} {split} frame(s) got no usable VLM answer (rate limit? bad JSON?) "
            "and count as 'nothing found'. Re-run before trusting the VLM and hybrid rows."
        )
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
        method = result.errors[0]["method"]
        lines += ["", f"Error examples from {method} (thumbnails in the errors folder):"]
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
    stem = "vision_" + safe_label(label)
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
