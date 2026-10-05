"""Matching events to seeded bugs (spec 00 match rules). Eval may read labels.json; triage never."""

import re
from pathlib import Path

from qalab.eval.ground_truth import (
    build_ground_truth,
    feature_components,
    load_labels,
    rule_matches,
)
from qalab.io.runs import load_run
from qalab.models.event import Event
from qalab.models.labels import MatchRule

from .bench import DESIGN_DOC, REPO, SAMPLE_RUN, copy_run


def _event(**fields) -> Event:
    base = {"schema": "qalab.event/1", "run_id": "r", "seq": 1, "t": 1.0, "kind": "log"}
    base.update(fields)
    return Event.model_validate(base)


def test_each_rule_key_must_match() -> None:
    log = _event(
        level="error", message="Failed to load asset Assets/Audio/sfx_3.wav", stack="A.Load ()"
    )
    assert rule_matches(MatchRule(message_regex="^Failed to load asset"), log)
    assert rule_matches(MatchRule(stack_contains="A.Load"), log)
    assert not rule_matches(MatchRule(stack_contains="A.Load", message_regex="^Nope"), log)
    assert not rule_matches(MatchRule(stack_contains="B.Save"), log)
    assert not rule_matches(MatchRule(detector="perf_spike"), log), "logs are not detector events"
    assert not rule_matches(MatchRule(), log), "an empty rule matches nothing"


def test_near_uses_the_ground_plane_only() -> None:
    fall = _event(
        kind="detector",
        pos=(35.2, -12.0, 1.1),
        data={"detector": "fell_out_of_world", "severity": "critical"},
    )
    rule = MatchRule(detector="fell_out_of_world", near=(35.0, 0.0, 1.0), radius=4.0)
    assert rule_matches(rule, fall), "12 m below doesn't matter: x and z only"
    far = fall.model_copy(update={"pos": (40.0, 0.0, 1.0)})
    assert not rule_matches(rule, far)
    nowhere = fall.model_copy(update={"pos": None})
    assert not rule_matches(rule, nowhere)


def test_visual_rules_never_match_raw_events() -> None:
    shot = _event(kind="screenshot", data={"path": "shots/000004.png", "reason": "periodic"})
    assert not rule_matches(MatchRule(visual_label="missing_texture"), shot)


def test_sample_run_ground_truth_matches_expected_md(tmp_path: Path) -> None:
    run = load_run(SAMPLE_RUN)
    truth = build_ground_truth(
        {run.run.run_id: run.events}, {run.run.run_id: load_labels(SAMPLE_RUN)}
    )
    rid = run.run.run_id
    expected = {
        8: "SB01",
        11: "SB02",
        15: "SB02",
        18: "SB04",
        19: "SB03",
        26: "SB03",
        21: "SB08",
        23: "SB06",
        13: "SB13",
        16: "SB13",
        28: "SB13",
        30: "SB14",
        31: "SB14",
    }
    for seq, bug in expected.items():
        assert truth.bug_of(rid, seq) == bug, seq
    assert truth.bug_of(rid, 34) is None, "the info checkpoint log is no bug"
    assert truth.ambiguous == []
    assert {"SB09", "SB10"} <= set(truth.bugs), (
        "visual seeds are known even though no event matches them"
    )


def test_runs_without_labels_are_listed(tmp_path: Path) -> None:
    unlabelled = copy_run(tmp_path, "20261005T110000Z-s9", labels=False)
    run = load_run(unlabelled)
    assert load_labels(unlabelled) is None
    truth = build_ground_truth({run.run.run_id: run.events}, {run.run.run_id: None})
    assert truth.runs_without_labels == [run.run.run_id] and not truth.event_bug


def test_a_seed_that_did_not_fire_in_a_run_is_not_its_ground_truth(tmp_path: Path) -> None:
    run_dir = copy_run(tmp_path, "20261005T110000Z-s9")
    labels = load_labels(run_dir)
    labels.seeded_bugs = [b for b in labels.seeded_bugs if b.bug_id != "SB01"]
    run = load_run(run_dir)
    truth = build_ground_truth({run.run.run_id: run.events}, {run.run.run_id: labels})
    assert truth.bug_of(run.run.run_id, 8) is None


def test_feature_components_from_the_design_doc() -> None:
    components = feature_components(DESIGN_DOC.read_text(encoding="utf-8"))
    assert components["Doors"] == ["SeededDoor", "Interactor"]
    assert components["Combat math"] == ["DamageCalculator", "SpeedModel", "MathUtil"]
    assert components["Settings menu"] == ["SeededSettingsMenu"]
    assert components["Performance"] == [], "no Component line"


# '"SB06", "Level geometry", "S2",' → ("SB06", "Level geometry")
_CATALOG_ENTRY = re.compile(r'"(SB\d\d)", "([^"]+)", "S[1-4]"')
CATALOG = (
    REPO / "unity" / "QALabSandbox" / "Assets" / "Sandbox" / "Scripts" / "SandboxSeedCatalog.cs"
)


def test_every_catalog_feature_is_a_heading() -> None:
    # A typo in a catalog feature would silently zero hit@k and "component correct" for that bug.
    entries = dict(_CATALOG_ENTRY.findall(CATALOG.read_text(encoding="utf-8")))
    assert len(entries) >= 15, "SB02's entry is a learning task; every other seed is listed"
    headings = feature_components(DESIGN_DOC.read_text(encoding="utf-8"))
    assert {bug: f for bug, f in entries.items() if f not in headings} == {}
