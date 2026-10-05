"""``visual_findings.jsonl`` (spec 00, ``qalab.visual_finding/1``): writing it, and turning it into
in-memory ``visual:<label>`` detector events for triage (spec 03, "Integration with triage").

``events.jsonl`` is never edited. Each label scoring ≥ 0.5 becomes a detector event that borrows the
matching screenshot event's ``seq``, so every evidence reference stays a real ``{run_id, seq}``.
"""

from __future__ import annotations

import json
import logging
from collections.abc import Iterable, Sequence
from pathlib import Path

from pydantic import ValidationError

from qalab.models.event import Event
from qalab.models.schemas import first_error
from qalab.models.visual import VisualFinding

log = logging.getLogger(__name__)

FINDINGS_FILE = "visual_findings.jsonl"
MIN_SCORE = 0.5
SEVERITY = {
    "black_screen": "major",
    "missing_texture": "minor",
    "ui_overflow": "minor",
    "placeholder_ui": "minor",
}


def write_findings(run_dir: Path, findings: Iterable[VisualFinding]) -> Path:
    """Replace the run's ``visual_findings.jsonl`` (UTF-8, LF, one JSON object per line)."""
    path = Path(run_dir) / FINDINGS_FILE
    lines = [
        json.dumps(f.to_json_dict(), separators=(",", ":"), ensure_ascii=False) for f in findings
    ]
    path.write_bytes(("".join(line + "\n" for line in lines)).encode("utf-8"))
    return path


def read_findings(run_dir: Path) -> list[VisualFinding]:
    """The findings of a run; invalid lines are skipped with a warning (never fatal)."""
    path = Path(run_dir) / FINDINGS_FILE
    if not path.is_file():
        return []
    findings: list[VisualFinding] = []
    with path.open(encoding="utf-8") as f:
        for number, line in enumerate(f, start=1):
            if not line.strip():
                continue
            try:
                raw = json.loads(line)
                error = first_error("visual_finding", raw)
                if error:
                    raise ValueError(error)
                findings.append(VisualFinding.model_validate(raw))
            except (ValueError, ValidationError) as exc:
                log.warning("%s line %d skipped: %s", path, number, exc)
    return findings


def validate_findings_file(run_dir: Path) -> list[str]:
    """``line N: <error>`` for each bad line of ``visual_findings.jsonl`` (none if absent)."""
    path = Path(run_dir) / FINDINGS_FILE
    if not path.is_file():
        return []
    errors: list[str] = []
    with path.open(encoding="utf-8") as f:
        for number, line in enumerate(f, start=1):
            if not line.strip():
                continue
            try:
                error = first_error("visual_finding", json.loads(line))
            except json.JSONDecodeError as exc:
                error = f"not JSON: {exc.msg}"
            if error:
                errors.append(f"line {number}: {error}")
    return errors


def visual_events(findings: Sequence[VisualFinding], events: Sequence[Event]) -> list[Event]:
    """One detector event per finding label with score ≥ 0.5, attached to its screenshot event."""
    shots = {
        (e.data or {}).get("path"): e
        for e in events
        if e.kind == "screenshot" and e.data is not None
    }
    out: list[Event] = []
    for finding in findings:
        shot = shots.get(finding.shot)
        if shot is None:
            log.warning(
                "%s: no screenshot event for %s; finding ignored", finding.run_id, finding.shot
            )
            continue
        for found in finding.labels:
            if found.score < MIN_SCORE:
                continue
            details: dict[str, object] = {"score": found.score, "method": finding.method}
            if finding.model:
                details["model"] = finding.model
            if found.region:
                details["region"] = list(found.region)
            if finding.explanation:
                details["explanation"] = finding.explanation
            out.append(
                Event.model_validate(
                    {
                        "schema": "qalab.event/1",
                        "run_id": shot.run_id,
                        "seq": shot.seq,
                        "t": shot.t,
                        "ts": shot.ts,
                        "frame": shot.frame,
                        "kind": "detector",
                        "scene": shot.scene,
                        "pos": shot.pos,
                        "data": {
                            "detector": f"visual:{found.label}",
                            "severity": SEVERITY[found.label],
                            "details": details,
                            "screenshot": finding.shot,
                        },
                    }
                )
            )
    return out
