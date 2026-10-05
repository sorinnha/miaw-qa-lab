"""``qalab vision analyze``: label every screenshot of a run and write ``visual_findings.jsonl``.

Inference only: reads ``run.json``, ``events.jsonl`` and ``shots/``. It never opens ``labels.json``
(the leakage test checks this together with triage).
"""

from __future__ import annotations

import logging
import time
from collections import Counter
from collections.abc import Sequence
from dataclasses import dataclass, field
from pathlib import Path
from typing import Literal

from qalab.io.runs import LoadedRun
from qalab.llm.base import LLMProvider
from qalab.models.event import Event
from qalab.models.visual import FoundLabel, VisualFinding
from qalab.vision import heuristics, vlm
from qalab.vision.findings import MIN_SCORE, write_findings
from qalab.vision.hybrid import HybridPolicy, event_gaps
from qalab.vision.images import load_rgb
from qalab.vision.ml import MlModel

log = logging.getLogger(__name__)

Method = Literal["heuristic", "vlm", "ml", "hybrid"]
METHODS: tuple[Method, ...] = ("heuristic", "vlm", "ml", "hybrid")


@dataclass
class AnalyzeOptions:
    method: Method = "heuristic"
    provider: LLMProvider | None = None  # vlm and hybrid
    ml_model: MlModel | None = None  # ml
    thresholds: heuristics.Thresholds = field(default_factory=heuristics.Thresholds)
    hybrid_every_n: int = 5
    hybrid_window_s: float = 2.0
    max_retries: int = 2


@dataclass
class RunSummary:
    run_id: str
    path: Path
    frames: int
    missing: int  # screenshot events whose file is gone
    labels: Counter[str]  # labels with score ≥ 0.5, the ones triage turns into bugs
    vlm_calls: int
    vlm_errors: int
    seconds: float


def ui_event_times(events: Sequence[Event]) -> list[float]:
    """Times of UI actions (``ui_path`` set) and detector events: where UI bugs tend to show."""
    times = []
    for e in events:
        if e.kind == "detector" and not str((e.data or {}).get("detector", "")).startswith(
            "visual:"
        ):
            times.append(e.t)
        elif e.kind == "action" and (e.data or {}).get("ui_path"):
            times.append(e.t)
    return times


def analyze_run(loaded: LoadedRun, options: AnalyzeOptions) -> RunSummary:
    """Analyze every screenshot event of one run, in order, and write its findings file."""
    if options.method in ("vlm", "hybrid") and options.provider is None:
        raise ValueError(f"--method {options.method} needs a vision provider (not --provider none)")
    if options.method == "ml" and options.ml_model is None:
        raise ValueError("--method ml needs --ml-model (train it with qalab vision train-ml)")
    started = time.perf_counter()
    shots = [e for e in loaded.events if e.kind == "screenshot" and e.data]
    gaps = event_gaps([e.t for e in shots], ui_event_times(loaded.events))
    policy = HybridPolicy(options.hybrid_every_n, options.hybrid_window_s)
    findings: list[VisualFinding] = []
    labels: Counter[str] = Counter()
    missing = calls = errors = 0
    for event, gap in zip(shots, gaps, strict=True):
        shot = event.screenshot().path
        file = loaded.run_dir / shot
        if not file.is_file():
            missing += 1
            log.warning("%s: %s is missing; skipped", loaded.run.run_id, shot)
            continue
        rgb = load_rgb(file)
        frame_started = time.perf_counter()
        found: list[FoundLabel]
        explanation: str | None = None
        model: str | None = None
        cached: bool | None = None
        latency: float | None = None
        if options.method == "heuristic":
            found = heuristics.analyze(rgb, options.thresholds)
        elif options.method == "ml":
            assert options.ml_model is not None  # checked at the top
            found = options.ml_model.predict(rgb)
            model = "logistic-regression"
        else:
            found = (
                heuristics.analyze(rgb, options.thresholds) if options.method == "hybrid" else []
            )
            if options.method == "vlm" or policy.should_call_vlm(bool(found), gap):
                assert options.provider is not None  # checked at the top
                verdict = vlm.analyze(
                    rgb, options.provider, shot, event.scene, event.t, options.max_retries
                )
                calls += 1
                errors += verdict.error is not None
                found = found + verdict.labels
                explanation = verdict.explanation or verdict.error
                model, cached, latency = verdict.model, verdict.cached, verdict.latency_ms
        if latency is None:
            latency = round((time.perf_counter() - frame_started) * 1000, 2)
        labels.update(f.label for f in found if f.score >= MIN_SCORE)  # what triage will see
        findings.append(
            VisualFinding(
                run_id=loaded.run.run_id,
                shot=shot,
                t=event.t,
                scene=event.scene,
                pos=event.pos,
                method=options.method,
                labels=found,
                explanation=explanation,
                model=model,
                latency_ms=latency,
                cached=cached,
            )
        )
    path = write_findings(loaded.run_dir, findings)
    return RunSummary(
        run_id=loaded.run.run_id,
        path=path,
        frames=len(findings),
        missing=missing,
        labels=labels,
        vlm_calls=calls,
        vlm_errors=errors,
        seconds=round(time.perf_counter() - started, 2),
    )
