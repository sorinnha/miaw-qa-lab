"""M0 learning task: scripts/hello_events.py counts per kind and per log level."""

import importlib.util
import json
from pathlib import Path
from types import ModuleType

import pytest

REPO = Path(__file__).resolve().parents[2]
SCRIPT = REPO / "scripts" / "hello_events.py"
SAMPLE_EVENTS = REPO / "samples" / "sample_run" / "events.jsonl"


def _load_script() -> ModuleType:
    spec = importlib.util.spec_from_file_location("hello_events", SCRIPT)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _expected() -> tuple[dict[str, int], dict[str, int]]:
    per_kind: dict[str, int] = {}
    per_level: dict[str, int] = {}
    for line in SAMPLE_EVENTS.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        event = json.loads(line)
        per_kind[event["kind"]] = per_kind.get(event["kind"], 0) + 1
        if event["kind"] == "log":
            per_level[event["level"]] = per_level.get(event["level"], 0) + 1
    return per_kind, per_level


def test_script_exists_and_has_the_entry_points() -> None:
    module = _load_script()
    assert callable(module.count_events) and callable(module.main)
    assert module.DEFAULT_EVENTS == SAMPLE_EVENTS


def test_counts_match_the_sample_run() -> None:
    per_kind, per_level = _load_script().count_events(SAMPLE_EVENTS)
    expected_kind, expected_level = _expected()
    assert dict(per_kind) == expected_kind
    assert dict(per_level) == expected_level
    assert sum(per_kind.values()) == 38
    assert per_kind["log"] == 12 and per_level["warning"] == 3


def test_blank_lines_are_skipped(tmp_path: Path) -> None:
    path = tmp_path / "events.jsonl"
    path.write_text('{"kind":"metric"}\n\n{"kind":"log","level":"error"}\n', encoding="utf-8")
    per_kind, per_level = _load_script().count_events(path)
    assert dict(per_kind) == {"metric": 1, "log": 1} and dict(per_level) == {"error": 1}


EXPECTED_OUTPUT = """\
kind   action       9
kind   detector     2
kind   log         12
kind   marker       5
kind   metric       5
kind   screenshot   5
level  error        4
level  exception    4
level  info         1
level  warning      3
"""


def test_main_prints_the_documented_tables(capsys: pytest.CaptureFixture[str]) -> None:
    assert _load_script().main([str(SAMPLE_EVENTS)]) == 0
    assert capsys.readouterr().out == EXPECTED_OUTPUT


def test_main_defaults_to_the_sample_run(capsys: pytest.CaptureFixture[str]) -> None:
    assert _load_script().main([]) == 0
    assert capsys.readouterr().out == EXPECTED_OUTPUT


def test_docstring_shows_the_real_expected_output() -> None:
    doc = _load_script().__doc__ or ""
    for line in EXPECTED_OUTPUT.splitlines():
        assert f"    {line}\n" in doc, line


def test_a_truncated_last_line_is_skipped_and_reported(
    tmp_path: Path, capsys: pytest.CaptureFixture[str]
) -> None:
    # A crashed run can end with half a line; the counts of the complete lines still come out.
    events = tmp_path / "events.jsonl"
    events.write_text('{"kind":"log","level":"error"}\n{"kind":"me', encoding="utf-8")
    per_kind, per_level = _load_script().count_events(events)
    assert dict(per_kind) == {"log": 1} and dict(per_level) == {"error": 1}
    assert "skipped line 2: not JSON" in capsys.readouterr().err
