"""Spec 02 §1: discover run folders, stream and validate ``events.jsonl``, load ``run.json``.

This module never opens ``labels.json``: that file is ground truth for ``qalab eval`` only.
"""

from __future__ import annotations

import glob
import json
import logging
from collections.abc import Iterable, Iterator
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any

from pydantic import ValidationError

from qalab.models.event import Event
from qalab.models.run import Run
from qalab.models.schemas import first_error
from qalab.vision.findings import read_findings, visual_events

log = logging.getLogger(__name__)

INVALID_WARN_RATIO = 0.01


@dataclass
class InvalidLine:
    line: int  # 1-based line number in events.jsonl
    error: str


@dataclass
class ValidationReport:
    """What happened while reading one run; written to ``validation_report.json``."""

    run_dir: str
    run_id: str | None = None
    run_json_error: str | None = None
    total_lines: int = 0
    valid_events: int = 0
    out_of_order: int = 0
    invalid: list[InvalidLine] = field(default_factory=list)

    @property
    def invalid_ratio(self) -> float:
        return len(self.invalid) / self.total_lines if self.total_lines else 0.0

    @property
    def ok(self) -> bool:
        return self.run_json_error is None and not self.invalid

    def to_dict(self) -> dict[str, Any]:
        data = asdict(self)
        data["invalid_ratio"] = round(self.invalid_ratio, 4)
        data["ok"] = self.ok
        return data


@dataclass
class LoadedRun:
    run_dir: Path
    run: Run
    events: list[Event]  # sorted by seq
    report: ValidationReport

    @property
    def suspected_crash(self) -> bool:
        return self.run.suspected_crash


def discover_runs(patterns: Iterable[str | Path]) -> list[Path]:
    """Expand paths and globs to the run folders they name (a folder with ``run.json``)."""
    found: list[Path] = []
    for pattern in patterns:
        text = str(pattern)
        matches = [Path(m) for m in sorted(glob.glob(text))] or [Path(text)]
        for path in matches:
            if (path / "run.json").is_file():
                found.append(path)
            elif path.is_dir():
                found.extend(p.parent for p in sorted(path.glob("*/run.json")))
    return sorted(set(found))


def load_run_meta(run_dir: Path) -> Run:
    """Parse ``run.json``, checking the JSON Schema first so errors name the schema rule."""
    with (run_dir / "run.json").open(encoding="utf-8") as f:
        raw = json.load(f)
    error = first_error("run", raw)
    if error:
        raise ValueError(f"run.json: {error}")
    return Run.model_validate(raw)


def parse_event_line(text: str) -> Event:
    """One JSONL line → Event. Raises ValueError with a readable reason."""
    try:
        raw = json.loads(text)
    except json.JSONDecodeError as exc:
        raise ValueError(f"not JSON: {exc.msg}") from exc
    error = first_error("event", raw)
    if error:
        raise ValueError(error)
    try:
        return Event.model_validate(raw)
    except ValidationError as exc:
        raise ValueError(exc.errors()[0]["msg"]) from exc


def iter_events(run_dir: Path, report: ValidationReport) -> Iterator[Event]:
    """Stream valid events in file order; bad lines are recorded in ``report`` and skipped.

    C# comparison: a generator (``yield``) works like ``IEnumerable<T>`` with ``yield return``.
    """
    previous_seq = -1
    with (run_dir / "events.jsonl").open(encoding="utf-8") as f:
        for number, line in enumerate(f, start=1):
            if not line.strip():
                continue
            report.total_lines += 1
            try:
                event = parse_event_line(line)
            except ValueError as exc:
                report.invalid.append(InvalidLine(line=number, error=str(exc)))
                continue
            if event.seq <= previous_seq:
                report.out_of_order += 1
            previous_seq = event.seq
            report.valid_events += 1
            yield event


def load_run(run_dir: Path) -> LoadedRun:
    """Load one run folder: metadata, validated events sorted by ``seq``, and a report. When
    ``visual_findings.jsonl`` exists its labels are added as ``visual:<label>`` detector events."""
    run_dir = Path(run_dir)
    report = ValidationReport(run_dir=str(run_dir))
    run = load_run_meta(run_dir)
    report.run_id = run.run_id
    events = sorted(iter_events(run_dir, report), key=lambda e: e.seq)
    # Spec 03: vision findings join as in-memory visual:<label> detector events that share their
    # screenshot's seq (stable sort: each comes right after its screenshot event).
    visual = visual_events(read_findings(run_dir), events)
    if visual:
        events = sorted(events + visual, key=lambda e: e.seq)
    if report.invalid_ratio > INVALID_WARN_RATIO:
        log.warning(
            "%s: %d of %d event lines are invalid",
            run.run_id,
            len(report.invalid),
            report.total_lines,
        )
    if run.suspected_crash:
        log.warning("%s: run.json has no ended_at, flagging a suspected crash", run.run_id)
    return LoadedRun(run_dir=run_dir, run=run, events=events, report=report)


def validate_run(run_dir: Path) -> ValidationReport:
    """Schema-check a run folder without keeping events in memory (used by ``qalab validate``)."""
    run_dir = Path(run_dir)
    report = ValidationReport(run_dir=str(run_dir))
    try:
        report.run_id = load_run_meta(run_dir).run_id
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        report.run_json_error = str(exc)
    if (run_dir / "events.jsonl").is_file():
        for _ in iter_events(run_dir, report):
            pass
    else:
        report.invalid.append(InvalidLine(line=0, error="events.jsonl is missing"))
    return report


def validate_labels_file(run_dir: Path) -> str | None:
    """Schema-check ``labels.json`` when present. Only ``validate`` and ``eval`` call this."""
    path = run_dir / "labels.json"
    if not path.is_file():
        return None
    with path.open(encoding="utf-8") as f:
        return first_error("labels", json.load(f))
