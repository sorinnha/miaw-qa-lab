"""visual_findings.jsonl, ``qalab vision analyze`` and the triage integration (spec 03).

The VLM-only paths need no learning task; the heuristic and hybrid paths run ``magenta_ratio`` and
triage runs ``normalize_message`` (both YOU WRITE), so those tests carry the marker.
"""

import builtins
import json
from pathlib import Path

import pytest
from typer.testing import CliRunner

from qalab.cli import app
from qalab.eval.ground_truth import build_ground_truth, load_labels
from qalab.io.runs import load_run
from qalab.io.writers import copy_attachments
from qalab.llm.fake import FakeProvider
from qalab.models.visual import FoundLabel, VisualFinding
from qalab.triage.cluster import Cluster, ClusterMember
from qalab.triage.context import build_context
from qalab.triage.report_llm import generate_report
from qalab.vision.analyze import AnalyzeOptions, analyze_run
from qalab.vision.findings import (
    FINDINGS_FILE,
    read_findings,
    validate_findings_file,
    visual_events,
    write_findings,
)

from ..eval.bench import copy_run

runner = CliRunner()
SEEN = {
    "shots/000004.png": [{"label": "missing_texture", "score": 0.9}],
    "shots/000005.png": [
        {"label": "black_screen", "score": 0.97},
        {"label": "ui_overflow", "score": 0.3},
    ],
}


def _finding(shot: str, *labels: tuple[str, float], run_id: str = "r") -> VisualFinding:
    return VisualFinding(
        run_id=run_id,
        shot=shot,
        method="vlm",
        labels=[FoundLabel(label=label, score=score) for label, score in labels],  # type: ignore[arg-type]
    )


def test_findings_round_trip_and_validate(tmp_path: Path) -> None:
    write_findings(
        tmp_path,
        [_finding("shots/000004.png", ("missing_texture", 0.9)), _finding("shots/000001.png")],
    )
    text = (tmp_path / FINDINGS_FILE).read_bytes()
    assert b"\r" not in text and text.endswith(b"\n")
    findings = read_findings(tmp_path)
    assert [f.shot for f in findings] == ["shots/000004.png", "shots/000001.png"]
    assert validate_findings_file(tmp_path) == []
    (tmp_path / FINDINGS_FILE).write_text(
        '{"schema": "qalab.visual_finding/1"}\nnot json\n', "utf-8"
    )
    errors = validate_findings_file(tmp_path)
    assert len(errors) == 2 and errors[1].startswith("line 2: not JSON")
    assert read_findings(tmp_path) == [], "bad lines are skipped, never fatal"


def test_visual_events_borrow_the_screenshot_seq(tmp_path: Path) -> None:
    run = load_run(copy_run(tmp_path, "20261005T103000Z-s42"))
    findings = [
        _finding(
            "shots/000005.png", ("black_screen", 0.97), ("ui_overflow", 0.3), run_id=run.run.run_id
        ),
        _finding("shots/999999.png", ("missing_texture", 0.9), run_id=run.run.run_id),
    ]
    events = visual_events(findings, run.events)
    assert len(events) == 1, "score 0.3 is under 0.5, and an unknown shot has no evidence"
    shot = next(
        e for e in run.events if e.kind == "screenshot" and e.data["path"] == "shots/000005.png"
    )
    visual = events[0]
    assert (visual.seq, visual.t, visual.pos) == (shot.seq, shot.t, shot.pos)
    assert visual.data["detector"] == "visual:black_screen" and visual.data["severity"] == "major"
    assert visual.data["screenshot"] == "shots/000005.png"


def test_load_run_adds_visual_events_after_their_screenshot(tmp_path: Path) -> None:
    run_dir = copy_run(tmp_path, "20261005T103000Z-s42")
    before = load_run(run_dir).events
    write_findings(
        run_dir,
        [_finding("shots/000004.png", ("missing_texture", 0.9), run_id="20261005T103000Z-s42")],
    )
    after = load_run(run_dir).events
    assert len(after) == len(before) + 1
    index = next(
        i
        for i, e in enumerate(after)
        if e.kind == "detector" and e.data["detector"].startswith("visual:")
    )
    assert after[index - 1].kind == "screenshot" and after[index - 1].seq == after[index].seq
    assert (run_dir / "events.jsonl").read_text("utf-8").count("visual:") == 0, (
        "events.jsonl is never edited"
    )


def test_the_report_leads_with_the_clearest_screenshot(tmp_path: Path) -> None:
    run_dir = copy_run(tmp_path, "20261005T103000Z-s42")
    write_findings(
        run_dir,
        [
            _finding("shots/000002.png", ("black_screen", 0.6), run_id="20261005T103000Z-s42"),
            _finding("shots/000005.png", ("black_screen", 0.97), run_id="20261005T103000Z-s42"),
        ],
    )
    loaded = load_run(run_dir)
    members = [
        ClusterMember.from_event(e)
        for e in loaded.events
        if e.kind == "detector" and e.detector().detector == "visual:black_screen"
    ]
    cluster = Cluster(
        signature="0123456789ab",
        kind="visual",
        members=members,
        detector="visual:black_screen",
        detector_severity="major",
    )
    runs = {loaded.run.run_id: loaded}
    report = generate_report(cluster, build_context(cluster, runs), "QAL-0001", None)
    report = copy_attachments(report, cluster, runs, tmp_path / "out")
    assert report.attachments == [
        "shots/20261005T103000Z-s42/000005.png",
        "shots/20261005T103000Z-s42/000002.png",
    ], "score 0.97 first, although 000002 came earlier"


def test_two_labels_on_one_frame_get_their_own_ground_truth(tmp_path: Path) -> None:
    rid = "20261005T103000Z-s42"
    run_dir = copy_run(tmp_path, rid)
    findings = [
        _finding("shots/000005.png", ("black_screen", 0.97), ("placeholder_ui", 0.8), run_id=rid)
    ]
    write_findings(run_dir, findings)
    run = load_run(run_dir)
    truth = build_ground_truth({rid: run.events}, {rid: load_labels(run_dir)})
    visual = {
        e.data["detector"]: truth.bug_of(e)
        for e in run.events
        if e.kind == "detector" and e.data["detector"].startswith("visual:")
    }
    # Same screenshot, same seq: the white box on a black frame must not inherit SB10.
    assert visual == {"visual:black_screen": "SB10", "visual:placeholder_ui": None}


def test_visual_ground_truth_needs_the_labelled_screenshot(tmp_path: Path) -> None:
    run_dir = copy_run(tmp_path, "20261005T103000Z-s42")
    rid = "20261005T103000Z-s42"
    write_findings(
        run_dir,
        [
            _finding(
                "shots/000004.png", ("missing_texture", 0.9), run_id=rid
            ),  # labels.json: SB09 shown
            _finding(
                "shots/000001.png", ("missing_texture", 0.8), run_id=rid
            ),  # clean frame: a false positive
        ],
    )
    run = load_run(run_dir)
    truth = build_ground_truth({rid: run.events}, {rid: load_labels(run_dir)})
    visual = [
        e
        for e in run.events
        if e.kind == "detector" and e.data["detector"] == "visual:missing_texture"
    ]
    assert [truth.bug_of(e) for e in visual] == [None, "SB09"]


def test_vlm_analysis_writes_valid_findings(tmp_path: Path) -> None:
    run = load_run(copy_run(tmp_path, "20261005T103000Z-s42"))
    summary = analyze_run(
        run, AnalyzeOptions(method="vlm", provider=FakeProvider(vision_labels=SEEN))
    )
    assert summary.frames == 5 and summary.vlm_calls == 5 and summary.missing == 0
    # ui_overflow at 0.3 is below triage's 0.5 cut: kept in the file, not counted as found.
    assert dict(summary.labels) == {"missing_texture": 1, "black_screen": 1}
    assert validate_findings_file(run.run_dir) == []
    lines = [json.loads(line) for line in summary.path.read_text("utf-8").splitlines()]
    assert {"label": "ui_overflow", "score": 0.3} in lines[4]["labels"]
    assert {line["method"] for line in lines} == {"vlm"} and lines[0]["model"] == "fake-1"


def test_analysis_needs_its_inputs(tmp_path: Path) -> None:
    run = load_run(copy_run(tmp_path, "20261005T103000Z-s42"))
    with pytest.raises(ValueError, match="vision provider"):
        analyze_run(run, AnalyzeOptions(method="vlm"))
    with pytest.raises(ValueError, match="--ml-model"):
        analyze_run(run, AnalyzeOptions(method="ml"))


def test_vision_analyze_never_opens_labels(tmp_path: Path, monkeypatch: pytest.MonkeyPatch) -> None:
    run_dir = copy_run(tmp_path, "20261005T103000Z-s42")
    real_open = builtins.open
    real_path_open = Path.open
    real_read_text = Path.read_text

    def guard(name: object) -> None:
        if str(name).endswith("labels.json"):
            raise AssertionError(f"inference opened {name}")

    def open_guard(file, *args, **kwargs):  # noqa: ANN001, ANN202
        guard(file)
        return real_open(file, *args, **kwargs)

    def path_open_guard(self, *args, **kwargs):  # noqa: ANN001, ANN202
        guard(self)
        return real_path_open(self, *args, **kwargs)

    def read_text_guard(self, *args, **kwargs):  # noqa: ANN001, ANN202
        guard(self)
        return real_read_text(self, *args, **kwargs)

    monkeypatch.setattr(builtins, "open", open_guard)
    monkeypatch.setattr(Path, "open", path_open_guard)
    monkeypatch.setattr(Path, "read_text", read_text_guard)
    result = runner.invoke(
        app,
        ["vision", "analyze", str(run_dir), "--method", "vlm", "--provider", "fake", "--no-cache"],
    )
    assert result.exit_code == 0, result.output
    assert (run_dir / FINDINGS_FILE).is_file()


@pytest.mark.youwrite
def test_heuristic_and_hybrid_analysis(tmp_path: Path) -> None:
    run = load_run(copy_run(tmp_path, "20261005T103000Z-s42"))
    heuristic = analyze_run(run, AnalyzeOptions(method="heuristic"))
    assert dict(heuristic.labels) == {"missing_texture": 1, "black_screen": 1}
    hybrid = analyze_run(
        run,
        AnalyzeOptions(
            method="hybrid", provider=FakeProvider(vision_labels=SEEN), hybrid_every_n=100
        ),
    )
    # The heuristics flag 000004 and 000005, so the VLM sees only the other three frames, and
    # only those near an event or the 1st of every N.
    # Shot 1 (16 s from any event) is the 1st of every 100 remaining frames; shot 2 (11 s) is not;
    # shot 3 is 0.01 s after the fall detector, inside the 2 s window.
    assert hybrid.vlm_calls == 2
    assert hybrid.labels["missing_texture"] == 1 and hybrid.labels["black_screen"] == 1


@pytest.mark.youwrite
def test_visual_bugs_reach_the_report(tmp_path: Path) -> None:
    run_dir = copy_run(tmp_path, "20261005T103000Z-s42")
    analyzed = runner.invoke(app, ["vision", "analyze", str(run_dir), "--method", "heuristic"])
    if isinstance(analyzed.exception, NotImplementedError):
        raise analyzed.exception
    assert analyzed.exit_code == 0, analyzed.output
    out = tmp_path / "report"
    result = runner.invoke(
        app, ["triage", "run", str(run_dir), "--provider", "none", "--out", str(out)]
    )
    if isinstance(result.exception, NotImplementedError):
        raise result.exception
    assert result.exit_code in (0, 3), result.output
    bugs = json.loads((out / "bugs.json").read_text("utf-8"))
    visual = [b for b in bugs if b["kind"] == "visual"]
    assert len(visual) == 2, "the magenta crate (000004) and the black frame (000005)"
    assert all(b["attachments"] for b in visual), "each visual bug carries its screenshot"
    html = (out / "report.html").read_text("utf-8")
    assert all(b["id"] in html for b in visual)
