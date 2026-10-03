"""Spec 02 §1: run loader."""

import json
import shutil
from pathlib import Path

import pytest

from qalab.io.runs import (
    ValidationReport,
    discover_runs,
    iter_events,
    load_run,
    validate_run,
)

REPO = Path(__file__).resolve().parents[3]
SAMPLE_RUN = REPO / "samples" / "sample_run"


def test_load_sample_run_sorted_and_clean() -> None:
    loaded = load_run(SAMPLE_RUN)
    assert loaded.run.run_id == "20261005T103000Z-s42"
    assert [e.seq for e in loaded.events] == list(range(38))
    assert loaded.report.ok and loaded.report.valid_events == 38
    assert loaded.suspected_crash is False


def test_iter_events_streams_in_file_order() -> None:
    report = ValidationReport(run_dir=str(SAMPLE_RUN))
    first = next(iter_events(SAMPLE_RUN, report))
    assert first.seq == 0 and first.kind == "marker"


def _copy_run(tmp_path: Path) -> Path:
    target = tmp_path / "run_copy"
    shutil.copytree(SAMPLE_RUN, target, ignore=shutil.ignore_patterns("labels.json", "shots"))
    return target


def test_invalid_lines_are_skipped_and_recorded(tmp_path: Path) -> None:
    run_dir = _copy_run(tmp_path)
    events_path = run_dir / "events.jsonl"
    lines = events_path.read_text(encoding="utf-8").splitlines()
    lines[5] = "{not json"
    bad = json.loads(lines[8])
    del bad["level"]
    lines[8] = json.dumps(bad)
    lines.append(lines[20])  # duplicate seq → out of order
    events_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    loaded = load_run(run_dir)
    assert loaded.report.valid_events == 37
    assert [b.line for b in loaded.report.invalid] == [6, 9]
    assert "JSON" in loaded.report.invalid[0].error
    assert "level" in loaded.report.invalid[1].error
    assert loaded.report.out_of_order == 1
    assert loaded.report.invalid_ratio > 0.01
    assert loaded.report.to_dict()["ok"] is False


def test_missing_ended_at_sets_suspected_crash(tmp_path: Path) -> None:
    run_dir = _copy_run(tmp_path)
    raw = json.loads((run_dir / "run.json").read_text(encoding="utf-8"))
    del raw["ended_at"]
    (run_dir / "run.json").write_text(json.dumps(raw), encoding="utf-8")
    assert load_run(run_dir).suspected_crash is True


def test_validate_run_reports_bad_run_json(tmp_path: Path) -> None:
    run_dir = _copy_run(tmp_path)
    (run_dir / "run.json").write_text('{"schema": "qalab.run/1"}', encoding="utf-8")
    report = validate_run(run_dir)
    assert report.run_json_error and "required" in report.run_json_error
    assert report.valid_events == 38 and not report.ok


_no_shots = shutil.ignore_patterns("shots")


def test_discover_runs_expands_globs_and_parents(tmp_path: Path) -> None:
    for name in ("a", "b"):
        shutil.copytree(SAMPLE_RUN, tmp_path / "runs" / name, ignore=_no_shots)
    (tmp_path / "runs" / "junk").mkdir()
    assert discover_runs([tmp_path / "runs"]) == [tmp_path / "runs" / "a", tmp_path / "runs" / "b"]
    assert discover_runs([str(tmp_path / "runs" / "*")]) == discover_runs([tmp_path / "runs"])
    assert discover_runs([tmp_path / "nowhere"]) == []


def test_loader_never_opens_labels(monkeypatch: pytest.MonkeyPatch) -> None:
    """Leakage guard for the loader itself; the full-pipeline version lives in tests/triage."""
    real_open = Path.open

    def guarded_open(self: Path, *args, **kwargs):
        if self.name == "labels.json":
            raise AssertionError("inference code opened labels.json")
        return real_open(self, *args, **kwargs)

    monkeypatch.setattr(Path, "open", guarded_open)
    assert load_run(SAMPLE_RUN).report.ok
