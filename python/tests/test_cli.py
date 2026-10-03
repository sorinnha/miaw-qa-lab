"""CLI smoke and leakage tests. The end-to-end ones call normalize_message → youwrite."""

import builtins
import json
from pathlib import Path

import pytest
from typer.testing import CliRunner

from qalab.cli import app

REPO = Path(__file__).resolve().parents[2]
SAMPLE_RUN = REPO / "samples" / "sample_run"
OUTPUT_FILES = (
    "bugs.json",
    "report.md",
    "bugs_jira.csv",
    "report.html",
    "clusters.json",
    "validation_report.json",
    "triage_meta.json",
)
runner = CliRunner()


def _invoke(args: list[str]):
    """Run the CLI; a YOU WRITE stub's NotImplementedError is re-raised so the test xfails."""
    result = runner.invoke(app, args)
    if isinstance(result.exception, NotImplementedError):
        raise result.exception
    return result


def test_validate_sample_run() -> None:
    result = runner.invoke(app, ["validate", str(SAMPLE_RUN)])
    assert result.exit_code == 0, result.output
    assert "38 events" in result.output


def test_validate_missing_folder_is_an_error(tmp_path: Path) -> None:
    assert runner.invoke(app, ["validate", str(tmp_path / "nope")]).exit_code == 2


def test_report_html_without_bugs_json(tmp_path: Path) -> None:
    assert runner.invoke(app, ["report", "html", str(tmp_path)]).exit_code == 2


@pytest.mark.youwrite
def test_triage_run_smoke(tmp_path: Path) -> None:
    out = tmp_path / "report"
    result = _invoke(
        [
            "triage",
            "run",
            str(SAMPLE_RUN),
            "--provider",
            "fake",
            "--out",
            str(out),
            "--no-cache",
            "--docs",
            str(REPO / "docs" / "sandbox_design.md"),
        ]
    )
    assert result.exit_code in (0, 3), result.output
    for name in OUTPUT_FILES:
        assert (out / name).is_file(), name
    bugs = json.loads((out / "bugs.json").read_text(encoding="utf-8"))
    assert len(bugs) == 8  # frame_tfidf on the sample run (EXPECTED.md)
    assert any(b.get("attachments") for b in bugs)
    assert runner.invoke(app, ["report", "html", str(out)]).exit_code == 0
    clusters = _invoke(["triage", "clusters", str(SAMPLE_RUN), "--cluster", "exact"])
    assert clusters.exit_code == 0 and "variant exact" in clusters.output


@pytest.mark.youwrite
def test_triage_never_opens_labels(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    """Leakage guard: labels.json must stay closed during qalab triage run."""

    def guard(name: str) -> None:
        if Path(str(name)).name == "labels.json":
            raise AssertionError("inference code opened labels.json")

    real_open, real_path_open, real_read_text = builtins.open, Path.open, Path.read_text

    def open_guard(file, *args, **kwargs):
        guard(file)
        return real_open(file, *args, **kwargs)

    def path_open_guard(self, *args, **kwargs):
        guard(self)
        return real_path_open(self, *args, **kwargs)

    def read_text_guard(self, *args, **kwargs):
        guard(self)
        return real_read_text(self, *args, **kwargs)

    monkeypatch.setattr(builtins, "open", open_guard)
    monkeypatch.setattr(Path, "open", path_open_guard)
    monkeypatch.setattr(Path, "read_text", read_text_guard)
    result = _invoke(
        ["triage", "run", str(SAMPLE_RUN), "--provider", "none", "--out", str(tmp_path / "o")]
    )
    assert result.exit_code in (0, 3), result.output
    # and the guard itself works
    with pytest.raises(AssertionError):
        (SAMPLE_RUN / "labels.json").read_text()
