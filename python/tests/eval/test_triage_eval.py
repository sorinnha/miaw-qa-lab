"""``qalab eval triage``: E1 clustering scores and E2/E3 report scores on a small benchmark.

Anything that clusters calls ``normalize_message`` and anything that scores clusters calls
``pairwise_prf`` (both YOU WRITE), so those tests carry the marker.
"""

from pathlib import Path

import pytest
from typer.testing import CliRunner

from qalab.cli import app
from qalab.eval.ground_truth import GroundTruth
from qalab.eval.triage_eval import (
    EvalResult,
    ReportScore,
    component_matches,
    field_completeness,
    has_bot_step,
    is_grounded,
    load_benchmark,
    majority_bug,
    score_clusters,
    to_markdown,
    write_result,
)
from qalab.models.bug import BugReport
from qalab.triage.pipeline import TriageOptions

from ..triage.fixtures import (
    SB01_DOORS,
    SB04_SPAWNER,
    SB14_COMBAT,
    load_sample,
    make_cluster,
)
from .bench import DESIGN_DOC, make_benchmark

runner = CliRunner()


def _truth_for_sample() -> GroundTruth:
    from qalab.eval.ground_truth import build_ground_truth, load_labels

    from .bench import SAMPLE_RUN

    run = load_sample()
    return build_ground_truth(
        {run.run.run_id: run.events}, {run.run.run_id: load_labels(SAMPLE_RUN)}
    )


def _report(**overrides) -> BugReport:
    base = {
        "schema": "qalab.bug_report/1",
        "id": "QAL-0001",
        "signature": "abcdef012345",
        "kind": "log",
        "title": "Door_02 throws when opened",
        "component": "SeededDoor",
        "severity": "S2",
        "priority": "P1",
        "score": 20.0,
        "summary": "s",
        "steps_to_reproduce": [{"text": "Open Door_02", "source": "template"}],
        "expected": "e",
        "actual": "a",
        "suspected_cause": "c",
        "frequency": {"count": 1, "runs": 1},
        "environment": {},
        "evidence": [{"run_id": "r", "seq": 8}],
        "confidence": 0.8,
        "needs_review": False,
        "generator": {"method": "llm", "provider": "fake", "model": "fake-1"},
    }
    base.update(overrides)
    return BugReport.model_validate(base)


# ---- report helpers (no learning task involved) --------------------------------------------------


def test_field_completeness_counts_blank_fields_and_steps() -> None:
    assert field_completeness(_report()) == 1.0
    assert field_completeness(_report(expected="  ", steps_to_reproduce=[])) == pytest.approx(5 / 7)


def test_grounding_reads_the_review_reasons() -> None:
    assert is_grounded(_report())
    assert is_grounded(_report(needs_review=True, review_reasons=["low confidence (0.40)"]))
    assert not is_grounded(
        _report(needs_review=True, review_reasons=["dropped unknown evidence ids: E9"])
    )
    assert not is_grounded(
        _report(
            needs_review=True,
            review_reasons=["step 2 cited unknown action ids (A7); marked inferred"],
        )
    )


def test_bot_steps_must_cite_real_actions() -> None:
    actions = {("r", 14), ("r", 22)}
    cited = _report(
        steps_to_reproduce=[
            {"text": "walk", "source": "bot_log", "action_refs": [{"run_id": "r", "seq": 14}]}
        ]
    )
    assert has_bot_step(cited, actions)
    wrong = _report(
        steps_to_reproduce=[
            {"text": "walk", "source": "bot_log", "action_refs": [{"run_id": "r", "seq": 15}]}
        ]
    )
    assert not has_bot_step(wrong, actions)
    assert not has_bot_step(_report(), actions), "template steps are not bot steps"


def test_component_match_is_case_insensitive_and_one_way() -> None:
    assert component_matches("SeededDoor (Door_02)", ["SeededDoor", "Interactor"])
    assert not component_matches("door", ["SeededDoor"]), "too vague: one way only"
    assert component_matches("seededdoor", ["SeededDoor"])
    assert not component_matches("", ["SeededDoor"])


def test_majority_bug_needs_more_than_half() -> None:
    truth = _truth_for_sample()
    sample = load_sample()
    assert majority_bug(make_cluster(sample, SB14_COMBAT), truth) == "SB14"
    assert majority_bug(make_cluster(sample, SB01_DOORS + SB04_SPAWNER), truth) is None, "a 1:1 mix"
    assert majority_bug(make_cluster(sample, [34]), truth) is None, "no labelled member"


def test_load_benchmark_keeps_unlabelled_runs_out_of_the_truth(tmp_path: Path) -> None:
    bench = load_benchmark(make_benchmark(tmp_path / "bench", runs=2, unlabelled=1))
    assert len(bench.runs) == 3
    assert len(bench.truth.runs_with_labels) == 2 and len(bench.truth.runs_without_labels) == 1
    assert bench.manifest is None


def test_a_benchmark_without_any_labels_is_an_error(tmp_path: Path) -> None:
    root = make_benchmark(tmp_path / "bench", runs=0, unlabelled=1)
    with pytest.raises(ValueError, match="labels.json"):
        load_benchmark(root)


def test_markdown_and_files_from_a_result(tmp_path: Path) -> None:
    result = EvalResult(
        benchmark="benchmarks/seeded_v1",
        created_at="2026-10-05T10:00:00Z",
        qalab_version="0.1.0",
        runs=2,
        runs_without_labels=[],
        seeded_bugs_present=["SB01"],
        ambiguous_events=0,
        manifest=None,
        skipped_variants={"frame_embed": "needs an embedding provider"},
        reports=ReportScore(
            label="fake",
            provider="fake",
            model="fake-1",
            rag=True,
            reports=3,
            labelled_reports=2,
            llm_reports=3,
            field_completeness=1.0,
            grounding_rate=1.0,
            repro_step_match=None,
            severity_agreement=0.5,
            severity_within_one=1.0,
            component_correct=0.5,
            retrieval_hit_at_k=1.0,
            k=3,
            latency_p50_ms=None,
            latency_p95_ms=None,
            tokens_per_report=12.0,
        ),
    )
    text = to_markdown(result)
    assert "| frame_embed | skipped: needs an embedding provider |" in text
    assert "| fake | 1.000 | 1.000 | n/a | 0.500 (1.000) | 0.500 | 1.000 | n/a / n/a | 12 |" in text
    written = write_result(result, tmp_path, "fake model")
    names = sorted(p.name for p in written)
    assert names == [
        "triage_fake_model.json",
        "triage_fake_model.md",
        "triage_fake_model_reports.png",
    ]


# ---- clustering scores (pairwise_prf, normalize_message) -----------------------------------------


@pytest.mark.youwrite
def test_scores_show_a_split_and_a_wrong_merge() -> None:
    truth = _truth_for_sample()
    sample = load_sample()
    split = [make_cluster(sample, [30]), make_cluster(sample, [31])]  # SB14 in two clusters
    merged = [make_cluster(sample, SB01_DOORS + SB04_SPAWNER)]  # two bugs in one
    noise = [make_cluster(sample, [34])]  # info log: no ground truth
    score = score_clusters(split + merged + noise, truth, "test")
    assert score.labelled_events == 4 and score.unlabelled_events == 1
    assert score.split_bugs == {"SB14": 2}
    assert [sorted(m["bugs"]) for m in score.mixed_clusters] == [["SB01", "SB04"]]
    # Pairs: predicted {8,18} (FP); true {30,31} (FN). TP 0 → P 0, R 0.
    assert (score.precision, score.recall, score.f1) == (0.0, 0.0, 0.0)
    assert (score.predicted_clusters, score.true_bugs, score.cluster_count_error) == (3, 3, 0)


@pytest.mark.youwrite
def test_cli_e1_on_a_two_run_benchmark(tmp_path: Path) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=2)
    out = tmp_path / "eval"
    result = runner.invoke(
        app,
        [
            "eval",
            "triage",
            str(bench),
            "--variants",
            "exact,frame_tfidf,tfidf_only",
            "--provider",
            "none",
            "--out",
            str(out),
            "--label",
            "e1",
        ],
    )
    if isinstance(result.exception, NotImplementedError):
        raise result.exception
    assert result.exit_code == 0, result.output
    assert (out / "triage_e1.json").is_file() and (out / "triage_e1_e1_prf.png").is_file()
    text = (out / "triage_e1.md").read_text(encoding="utf-8")
    # EXPECTED.md: exact splits SB14 (9 clusters for 8 bugs); tfidf_only merges SB01 + SB04.
    exact = next(line for line in text.splitlines() if line.startswith("| exact |"))
    assert "split: SB14×2" in exact and "9 / 8" in exact
    tfidf_only = next(line for line in text.splitlines() if line.startswith("| tfidf_only |"))
    assert "SB01+SB04" in tfidf_only
    frame = next(line for line in text.splitlines() if line.startswith("| frame_tfidf |"))
    assert "| 1.000 | 1.000 | 1.000 | 8 / 8 |" in frame


@pytest.mark.youwrite
def test_cli_reports_with_the_fake_provider(tmp_path: Path) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=1)
    out = tmp_path / "eval"
    result = runner.invoke(
        app,
        [
            "eval",
            "triage",
            str(bench),
            "--variants",
            "none",
            "--reports",
            "--provider",
            "fake",
            "--docs",
            str(DESIGN_DOC),
            "--out",
            str(out),
            "--label",
            "fake-rag",
            "--no-cache",
        ],
    )
    if isinstance(result.exception, NotImplementedError):
        raise result.exception
    assert result.exit_code == 0, result.output
    assert (out / "reports_fake-rag" / "bugs.json").is_file(), "the reports themselves are kept"
    text = (out / "triage_fake-rag.md").read_text(encoding="utf-8")
    assert "RAG on" in text and "map to a seeded bug" in text


def test_cli_rejects_unknown_variants(tmp_path: Path) -> None:
    result = runner.invoke(app, ["eval", "triage", str(tmp_path), "--variants", "magic"])
    assert result.exit_code == 2 and "unknown variant" in result.output


def test_options_type_is_reused() -> None:
    assert TriageOptions(out=Path("x")).variant == "frame_tfidf"
