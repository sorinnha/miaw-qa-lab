"""Spec 02 §8: prompt, retries, grounding checks, template fallback."""

from dataclasses import dataclass
from pathlib import Path

import pytest

from qalab.llm.base import LLMError
from qalab.llm.cache import CachedProvider, LLMCache
from qalab.llm.fake import FakeProvider
from qalab.models.schemas import first_error
from qalab.triage.context import build_context
from qalab.triage.prompts import load_prompt
from qalab.triage.report_llm import RETRY_SUFFIX, generate_report
from qalab.triage.report_template import template_report, template_title
from tests.triage.fixtures import (
    SB01_DOORS,
    SB03_ENEMY_REGISTRY,
    SB06_FELL,
    load_sample,
    make_cluster,
)


@dataclass
class Doc:
    chunk_id: str
    source: str
    heading: str
    text: str
    score: float = 0.9


DOCS = [
    Doc("sandbox_design.md#enemy-registry-0", "sandbox_design.md", "Enemy registry", "Enemies.")
]


@pytest.fixture(scope="module")
def runs():
    loaded = load_sample()
    return {loaded.run.run_id: loaded}


@pytest.fixture
def sb03(runs):
    loaded = next(iter(runs.values()))
    cluster = make_cluster(loaded, SB03_ENEMY_REGISTRY, priority="P2", score=9.5)
    return cluster, build_context(cluster, runs, docs=DOCS)


def _valid(report) -> None:
    assert first_error("bug_report", report.to_json_dict()) is None


def test_prompt_loads_and_renders(sb03) -> None:
    cluster, context = sb03
    prompt = load_prompt("triage_v1")
    assert prompt.version == "triage-v1"
    assert "senior game QA analyst" in prompt.render_system()
    user = prompt.render_user(**context.to_prompt_vars())
    for marker in ("[E1]", "[E2]", "[A1]", "[A6]", "[L5]", "[D1]", "KeyNotFoundException"):
        assert marker in user
    assert "(not available)" in user  # no code was given


def test_llm_report_is_grounded_and_valid(sb03) -> None:
    cluster, context = sb03
    provider = FakeProvider()
    report = generate_report(cluster, context, "QAL-0001", provider)
    _valid(report)
    assert report.generator.method == "llm" and report.generator.attempts == 1
    assert report.generator.prompt_version == "triage-v1" and report.generator.provider == "fake"
    assert report.priority == "P2" and report.score == 9.5  # never from the LLM
    assert report.evidence == [context.resolve("E1")]
    assert [s.source for s in report.steps_to_reproduce] == ["bot_log"] * 6
    assert report.steps_to_reproduce[2].action_refs[0].seq == 7
    assert report.docs_used == ["sandbox_design.md#enemy-registry-0"]
    assert report.needs_review is False and report.review_reasons is None
    assert report.frequency.count == 2 and report.environment.scenes == ["Sandbox_Level01"]


def test_invalid_json_twice_then_valid_means_three_attempts(sb03) -> None:
    cluster, context = sb03
    provider = FakeProvider(invalid_json_times=2)
    report = generate_report(cluster, context, "QAL-0001", provider)
    _valid(report)
    assert report.generator.method == "llm" and report.generator.attempts == 3
    assert len(provider.calls) == 3
    assert "failed validation" not in provider.calls[0]["user"]
    assert provider.calls[1]["user"].endswith(
        RETRY_SUFFIX.format(
            error="model output is not JSON: Expecting property name enclosed in double quotes"
        )
    )
    assert "Return only valid JSON." in provider.calls[2]["user"]


def test_invalid_three_times_falls_back_to_template(sb03) -> None:
    cluster, context = sb03
    report = generate_report(cluster, context, "QAL-0001", FakeProvider(invalid_json_times=3))
    _valid(report)
    assert report.generator.method == "template" and report.generator.attempts == 3
    assert report.needs_review is True
    assert report.review_reasons[0].startswith("LLM draft failed after 3 attempt(s)")
    assert report.expected == "unknown"
    assert all(s.source == "template" for s in report.steps_to_reproduce)


def test_schema_invalid_draft_retries_with_field_error(sb03) -> None:
    cluster, context = sb03
    provider = FakeProvider(script=['{"title": "x"}'])
    report = generate_report(cluster, context, "QAL-0001", provider)
    assert report.generator.method == "llm" and report.generator.attempts == 2
    assert "failed validation: component: Field required" in provider.calls[1]["user"]


def test_transport_error_falls_back_without_retrying(sb03) -> None:
    cluster, context = sb03

    class Down:
        name = "ollama"
        model = "m"
        calls = 0

        def complete_json(self, *args, **kwargs):
            self.calls += 1
            raise LLMError("unreachable")

        def embed(self, texts):
            raise AssertionError

    provider = Down()
    report = generate_report(cluster, context, "QAL-0001", provider)
    _valid(report)
    assert provider.calls == 1 and report.generator.attempts == 1
    assert report.generator.method == "template" and "provider error" in report.review_reasons[0]


def test_unknown_evidence_ids_are_dropped_and_flagged(sb03) -> None:
    cluster, context = sb03
    report = generate_report(
        cluster, context, "QAL-0001", FakeProvider(overrides={"evidence_ids": ["E2", "E9"]})
    )
    assert report.evidence == [context.resolve("E2")]
    assert report.needs_review and "dropped unknown evidence ids: E9" in report.review_reasons

    report = generate_report(
        cluster, context, "QAL-0001", FakeProvider(overrides={"evidence_ids": ["E9"]})
    )
    assert report.evidence == [context.resolve("E1")]
    assert "no valid evidence ids in the draft; used E1" in report.review_reasons


def test_bot_log_step_with_bad_action_id_becomes_inferred(sb03) -> None:
    cluster, context = sb03
    steps = [
        {"text": "ok", "source": "bot_log", "action_ids": ["A1", "A2"]},
        {"text": "bad", "source": "bot_log", "action_ids": ["A99"]},
        {"text": "empty", "source": "bot_log", "action_ids": []},
        {"text": "guess", "source": "inferred", "action_ids": []},
    ]
    provider = FakeProvider(overrides={"steps_to_reproduce": steps, "doc_ids": ["D1", "D7"]})
    report = generate_report(cluster, context, "QAL-0001", provider)
    _valid(report)
    assert [s.source for s in report.steps_to_reproduce] == [
        "bot_log",
        "inferred",
        "inferred",
        "inferred",
    ]
    assert [r.seq for r in report.steps_to_reproduce[0].action_refs] == [4, 6]
    assert report.steps_to_reproduce[1].action_refs is None
    reasons = " | ".join(report.review_reasons)
    assert "step 2 cited unknown action ids (A99)" in reasons
    assert "step 3 cited unknown action ids (none)" in reasons
    assert "dropped unknown doc ids: D7" in reasons
    assert report.docs_used == ["sandbox_design.md#enemy-registry-0"]


def test_severity_gap_and_low_confidence_are_flagged(sb03) -> None:
    cluster, context = sb03  # priority P2
    report = generate_report(
        cluster, context, "QAL-0001", FakeProvider(overrides={"severity": "S4", "confidence": 0.3})
    )
    reasons = " | ".join(report.review_reasons)
    assert "severity S4 disagrees with computed priority P2" in reasons
    assert "low confidence (0.30)" in reasons
    assert report.severity == "S4" and report.priority == "P2"

    fine = generate_report(cluster, context, "QAL-0001", FakeProvider(overrides={"severity": "S3"}))
    assert fine.needs_review is False


def test_long_title_is_cut_to_schema_limit(sb03) -> None:
    cluster, context = sb03
    report = generate_report(
        cluster, context, "QAL-0001", FakeProvider(overrides={"title": "T" * 150})
    )
    _valid(report)
    assert len(report.title) == 100


def test_cached_second_run_is_recorded(sb03, tmp_path: Path) -> None:
    cluster, context = sb03
    provider = CachedProvider(FakeProvider(), LLMCache(tmp_path / "llm.sqlite"), "triage-v1")
    assert generate_report(cluster, context, "QAL-0001", provider).generator.cached is False
    assert generate_report(cluster, context, "QAL-0001", provider).generator.cached is True


def test_template_report_shapes(runs) -> None:
    loaded = next(iter(runs.values()))
    cluster = make_cluster(loaded, SB01_DOORS, priority="P1", score=16.0)
    context = build_context(cluster, runs)
    report = generate_report(cluster, context, "QAL-0002", provider=None)
    _valid(report)
    assert report.title == template_title(cluster)
    assert report.title == ("SeededDoor: NullReferenceException in QALab.Sandbox.SeededDoor.Open")
    assert report.component == "SeededDoor" and report.severity == "S1"
    assert [s.text for s in report.steps_to_reproduce] == [
        "Step 1: move_to target=(4, 0, 2)",
        "Step 2: move_to target=(8, 0, 5)",
        "Step 3: interact object=Door_02",
    ]
    assert report.steps_to_reproduce[2].action_refs[0].seq == 7
    assert report.expected == "unknown" and report.generator.method == "template"
    assert report.evidence == [context.resolve("E1")] and report.needs_review is False

    fell = make_cluster(loaded, SB06_FELL, priority="P1")
    fell_report = template_report(fell, build_context(fell, runs), "QAL-0003")
    _valid(fell_report)
    assert fell_report.title.startswith("fell_out_of_world: fell_out_of_world in Sandbox_Level01")
    assert fell_report.attachments == ["shots/000003.png"]
