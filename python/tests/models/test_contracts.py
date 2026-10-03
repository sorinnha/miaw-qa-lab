"""Contract tests: the JSON Schemas and the pydantic models accept and reject the same fixtures."""

import json
from pathlib import Path

import pytest
from pydantic import ValidationError

from qalab.models import BugReport, Event, LLMBugDraft, Run
from qalab.models.schemas import first_error, load_schema, schemas_dir

REPO = Path(__file__).resolve().parents[3]
EXAMPLES = REPO / "schemas" / "examples"
SAMPLE_RUN = REPO / "samples" / "sample_run"


def _valid_lines() -> list[dict]:
    text = (EXAMPLES / "events_valid.jsonl").read_text(encoding="utf-8")
    return [json.loads(line) for line in text.splitlines() if line.strip()]


def _invalid_cases() -> list[dict]:
    return json.loads((EXAMPLES / "events_invalid.json").read_text(encoding="utf-8"))


def test_schemas_dir_points_at_repo() -> None:
    assert schemas_dir() == REPO / "schemas"
    assert load_schema("event")["$id"] == "qalab.event/1"


@pytest.mark.parametrize("raw", _valid_lines(), ids=lambda r: f"seq{r['seq']}-{r['kind']}")
def test_valid_events_pass_schema_and_model(raw: dict) -> None:
    assert first_error("event", raw) is None
    event = Event.model_validate(raw)
    assert event.to_json_dict() == {k: v for k, v in raw.items() if v is not None}


@pytest.mark.parametrize("case", _invalid_cases(), ids=lambda c: c["why"])
def test_invalid_events_fail_schema_and_model(case: dict) -> None:
    assert first_error("event", case["event"]) is not None, case["why"]
    with pytest.raises(ValidationError):
        Event.model_validate(case["event"])


def test_sample_run_validates() -> None:
    raw_run = json.loads((SAMPLE_RUN / "run.json").read_text(encoding="utf-8"))
    assert first_error("run", raw_run) is None
    run = Run.model_validate(raw_run)
    assert run.suspected_crash is False
    lines = (SAMPLE_RUN / "events.jsonl").read_text(encoding="utf-8").splitlines()
    for line in lines:
        raw = json.loads(line)
        assert first_error("event", raw) is None
        Event.model_validate(raw)
    assert len(lines) == 38


def test_run_without_ended_at_is_suspected_crash() -> None:
    raw_run = json.loads((SAMPLE_RUN / "run.json").read_text(encoding="utf-8"))
    del raw_run["ended_at"]
    assert Run.model_validate(raw_run).suspected_crash is True
    raw_run["ended_at"] = None
    assert Run.model_validate(raw_run).suspected_crash is True
    raw_run["ended_at"] = "not a date"
    assert first_error("run", raw_run) is not None
    with pytest.raises(ValidationError):
        Run.model_validate(raw_run)


def test_run_rejects_unknown_fields_and_bad_ids() -> None:
    raw_run = json.loads((SAMPLE_RUN / "run.json").read_text(encoding="utf-8"))
    raw_run["run_id"] = "has space"
    assert first_error("run", raw_run) is not None
    with pytest.raises(ValidationError):
        Run.model_validate(raw_run)


def test_event_typed_payload_views() -> None:
    raw = next(r for r in _valid_lines() if r["kind"] == "action")
    event = Event.model_validate(raw)
    assert event.action().step == raw["data"]["step"]
    with pytest.raises(ValueError):
        event.detector()


def _bug_report_dict() -> dict:
    return {
        "schema": "qalab.bug_report/1",
        "id": "QAL-0001",
        "signature": "0123456789ab",
        "kind": "log",
        "title": "Doors: NullReferenceException when opening Door_02",
        "component": "Doors",
        "severity": "S1",
        "priority": "P1",
        "score": 16.0,
        "score_breakdown": {"w": 5.0, "count": 1.0},
        "summary": "Opening a door throws.",
        "steps_to_reproduce": [
            {
                "text": "Interact with Door_02",
                "source": "bot_log",
                "action_refs": [{"run_id": "r1", "seq": 7}],
            }
        ],
        "expected": "unknown",
        "actual": "NullReferenceException",
        "suspected_cause": "unknown",
        "frequency": {"count": 1, "runs": 1, "first_seen_t": 8.21, "last_seen_t": 8.21},
        "environment": {"builds": ["0.1.0"], "scenes": ["Sandbox_Level01"]},
        "evidence": [{"run_id": "r1", "seq": 8}],
        "confidence": 0.5,
        "needs_review": False,
        "generator": {"method": "template", "attempts": 1},
    }


def test_bug_report_model_matches_schema() -> None:
    raw = _bug_report_dict()
    assert first_error("bug_report", raw) is None
    report = BugReport.model_validate(raw)
    assert report.to_json_dict() == raw


@pytest.mark.parametrize(
    "mutate",
    [
        lambda d: d.update(id="QAL-1"),
        lambda d: d.update(evidence=[]),
        lambda d: d.update(priority="P5"),
        lambda d: d.update(confidence=1.5),
        lambda d: d.update(extra_field=1),
        lambda d: d["generator"].update(method="human"),
    ],
    ids=["bad id", "no evidence", "bad priority", "confidence > 1", "extra field", "bad method"],
)
def test_bug_report_rejects_same_as_schema(mutate) -> None:
    raw = _bug_report_dict()
    mutate(raw)
    assert first_error("bug_report", raw) is not None
    with pytest.raises(ValidationError):
        BugReport.model_validate(raw)


def _draft_dict() -> dict:
    return {
        "title": "Doors: NullReferenceException when opening Door_02",
        "component": "Doors",
        "severity": "S1",
        "summary": "Opening a door throws.",
        "steps_to_reproduce": [{"text": "Interact", "source": "bot_log", "action_ids": ["A3"]}],
        "expected": "The door opens.",
        "actual": "NullReferenceException",
        "suspected_cause": "Likely: missing reference",
        "evidence_ids": ["E1"],
        "doc_ids": ["D1"],
        "confidence": 0.8,
    }


def test_llm_draft_model_matches_schema() -> None:
    raw = _draft_dict()
    assert first_error("llm_bug_draft", raw) is None
    assert LLMBugDraft.model_validate(raw).model_dump() == raw
    schema = load_schema("llm_bug_draft")
    assert "$defs" not in schema and schema["additionalProperties"] is False
    assert set(schema["required"]) == set(schema["properties"]), "every property is required"


@pytest.mark.parametrize(
    "mutate",
    [
        lambda d: d.pop("doc_ids"),
        lambda d: d.update(severity="P1"),
        lambda d: d["steps_to_reproduce"][0].update(source="template"),
        lambda d: d.update(priority="P1"),
    ],
    ids=["missing field", "bad severity", "template source", "extra field"],
)
def test_llm_draft_rejects_same_as_schema(mutate) -> None:
    raw = _draft_dict()
    mutate(raw)
    assert first_error("llm_bug_draft", raw) is not None
    with pytest.raises(ValidationError):
        LLMBugDraft.model_validate(raw)


def test_labels_model_matches_schema() -> None:
    from qalab.models.labels import Labels

    raw = json.loads((SAMPLE_RUN / "labels.json").read_text(encoding="utf-8"))
    assert first_error("labels", raw) is None
    labels = Labels.model_validate(raw)
    assert labels.seeded_bugs and labels.screenshots
    raw["seeded_bugs"][0]["match"] = {}
    assert first_error("labels", raw) is not None


def test_visual_finding_model_matches_schema() -> None:
    from qalab.models.visual import VisualFinding

    raw = {
        "schema": "qalab.visual_finding/1",
        "run_id": "r1",
        "shot": "shots/000004.png",
        "method": "heuristic",
        "labels": [{"label": "missing_texture", "score": 0.9, "region": [0, 0, 0.5, 0.5]}],
        "latency_ms": 3.0,
        "cached": False,
    }
    assert first_error("visual_finding", raw) is None
    assert VisualFinding.model_validate(raw).to_json_dict() == raw
    raw["labels"][0]["label"] = "blurry"
    assert first_error("visual_finding", raw) is not None
    with pytest.raises(ValidationError):
        VisualFinding.model_validate(raw)
