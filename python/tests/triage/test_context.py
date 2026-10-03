from dataclasses import dataclass

import pytest

from qalab.triage.context import (
    DEFAULT_BUDGET_CHARS,
    action_detail,
    build_context,
    evidence_items,
)
from tests.triage.fixtures import (
    SB01_DOORS,
    SB03_ENEMY_REGISTRY,
    SB06_FELL,
    SB13_AUDIO,
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


@pytest.fixture(scope="module")
def runs():
    loaded = load_sample()
    return {loaded.run.run_id: loaded}


def test_actions_are_exactly_those_before_first_occurrence(runs) -> None:
    loaded = next(iter(runs.values()))
    context = build_context(make_cluster(loaded, SB03_ENEMY_REGISTRY), runs)
    assert [a.seq for a in context.actions] == [4, 6, 7, 10, 14, 17]
    assert [a.id for a in context.actions] == [f"A{i}" for i in range(1, 7)]
    assert context.actions[2].detail == "object=Door_02"
    assert context.actions[0].detail == "target=(4, 0, 2)"
    assert context.resolve("A3").seq == 7
    # L: other log lines before seq 19, members excluded, last five
    assert [line.seq for line in context.logs] == [11, 13, 15, 16, 18]
    assert context.resolve("L5").seq == 18
    assert [e.seq for e in context.events] == [19, 26]
    assert context.resolve("E2").seq == 26
    assert context.facts["exception_type"] == "KeyNotFoundException"
    assert context.facts["top_frame"] == "QALab.Sandbox.SeededEnemyRegistry.Get"
    assert context.facts["count"] == 2 and context.facts["runs"] == 1
    assert context.facts["suspected_crash"] is False
    assert context.events[0].frames[0].startswith("QALab.Sandbox.SeededEnemyRegistry.Get (")
    with pytest.raises(KeyError):
        context.resolve("E9")


def test_first_bug_has_no_prior_logs(runs) -> None:
    loaded = next(iter(runs.values()))
    context = build_context(make_cluster(loaded, SB01_DOORS), runs)
    assert [a.seq for a in context.actions] == [4, 6, 7]
    assert context.logs == []
    assert len(context.events) == 1 and context.events[0].id == "E1"
    prompt_vars = context.to_prompt_vars()
    assert prompt_vars["actions"][0]["id"] == "A1" and '"signature"' in prompt_vars["cluster_json"]


def test_detector_cluster_facts_and_screenshots(runs) -> None:
    loaded = next(iter(runs.values()))
    context = build_context(make_cluster(loaded, SB06_FELL), runs)
    assert context.facts["detector"] == "fell_out_of_world"
    assert context.facts["detector_severity"] == "critical"
    assert context.detector_details["tile"] == "T_17"
    assert context.screenshots == ["shots/000003.png"]
    assert context.events[0].message.startswith("fell_out_of_world (critical)")
    assert "detector_details" in context.to_prompt_vars()["cluster_json"]


def test_evidence_picks_first_last_and_most_different(runs) -> None:
    loaded = next(iter(runs.values()))
    cluster = make_cluster(loaded, SB13_AUDIO)
    items = evidence_items(cluster)
    assert [e.seq for e in items] == [13, 16, 28]
    # A big cluster: first, last, then the 3 most different messages, never more than 5.
    big = make_cluster(loaded, [11, 13, 15, 16, 28, 30, 31])
    picked = [e.seq for e in evidence_items(big)]
    assert len(picked) == 5 and picked[0] == 11 and picked[-1] == 31
    assert 13 in picked and 30 in picked  # audio and combat differ most from inventory


def test_docs_and_code_are_mapped_and_capped(runs) -> None:
    loaded = next(iter(runs.values()))
    docs = [
        Doc(f"sandbox_design.md#h-{i}", "sandbox_design.md", f"H{i}", "x" * 700) for i in range(4)
    ]
    context = build_context(make_cluster(loaded, SB01_DOORS), runs, docs=docs, code="1: int a;")
    assert [d.id for d in context.docs] == ["D1", "D2", "D3"]
    assert all(len(d.text) == 600 for d in context.docs)
    assert context.doc_chunk_id("D2") == "sandbox_design.md#h-1"
    assert context.code == "1: int a;"
    assert context.chars <= DEFAULT_BUDGET_CHARS and context.trimmed == {}


def test_budget_trims_events_then_logs_then_docs(runs) -> None:
    loaded = next(iter(runs.values()))
    cluster = make_cluster(loaded, SB03_ENEMY_REGISTRY)
    docs = [Doc(f"d#{i}", "d.md", "H", "y" * 500) for i in range(3)]
    full = build_context(cluster, runs, docs=docs)
    assert len(full.events) == 2 and len(full.logs) == 5 and len(full.docs) == 3

    e_chars = full.events[1].chars
    # Budget just too small for E2: only E is trimmed.
    tight = build_context(cluster, runs, docs=docs, budget_chars=full.chars - 1)
    assert tight.trimmed == {"events": 1} and len(tight.logs) == 5 and len(tight.docs) == 3

    # Smaller still: E1 is kept, logs go next, then docs.
    tighter = build_context(cluster, runs, docs=docs, budget_chars=full.chars - e_chars - 1)
    assert tighter.trimmed["events"] == 1 and tighter.trimmed["logs"] >= 1
    assert len(tighter.docs) == 3 and len(tighter.events) == 1

    minimal = build_context(cluster, runs, docs=docs, budget_chars=1)
    assert minimal.trimmed == {"events": 1, "logs": 5, "docs": 3}
    assert len(minimal.events) == 1 and minimal.actions  # E1 and the A list always survive


def test_action_detail_prefers_ui_path(runs) -> None:
    loaded = next(iter(runs.values()))
    event = next(e for e in loaded.events if e.kind == "action").model_copy(
        update={"data": {"action": "click", "step": 1, "ui_path": "Canvas/Menu/Play"}}
    )
    assert action_detail(event) == "ui=Canvas/Menu/Play"
