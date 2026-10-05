"""``qalab eval triage``: E1 clustering scores and E2/E3 report scores on a small benchmark.

Anything that clusters calls ``normalize_message`` and anything that scores clusters calls
``pairwise_prf`` (both YOU WRITE), so those tests carry the marker.
"""

import json
from pathlib import Path

import pytest
from typer.testing import CliRunner

from qalab.cli import app
from qalab.eval.ground_truth import GroundTruth, feature_components
from qalab.eval.triage_eval import (
    Benchmark,
    EvalResult,
    ReportScore,
    component_matches,
    field_completeness,
    has_bot_step,
    is_grounded,
    load_benchmark,
    majority_bug,
    run_triage_eval,
    safe_label,
    score_clusters,
    score_reports,
    to_markdown,
    write_result,
)
from qalab.llm.base import LLMError
from qalab.llm.fake import FakeProvider
from qalab.models.bug import BugReport
from qalab.triage.context import build_context
from qalab.triage.pipeline import TriageOptions
from qalab.triage.report_llm import generate_report

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
    # The template's "unknown" fills the field but says nothing: it counts as empty.
    template_like = _report(expected="unknown", suspected_cause="Unknown ")
    assert field_completeness(template_like) == pytest.approx(5 / 7)


def test_grounding_reads_the_review_reasons() -> None:
    assert is_grounded(_report())
    fallback = {"method": "template", "provider": "fake", "attempts": 3}
    assert not is_grounded(_report(generator=fallback)), "a failed draft is not a grounded one"
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


@pytest.mark.parametrize(
    "overrides",
    [
        {"evidence_ids": ["E9"]},
        {"steps_to_reproduce": [{"text": "x", "source": "bot_log", "action_ids": ["A99"]}]},
        {"doc_ids": ["D7"]},
    ],
)
def test_grounding_reasons_match_what_the_report_writer_says(overrides: dict) -> None:
    # GROUNDING_REASONS matches report_llm's wording; if that wording changes, this test fails
    # instead of the grounding rate silently becoming 100%.
    sample = load_sample()
    cluster = make_cluster(sample, SB01_DOORS)
    context = build_context(cluster, {sample.run.run_id: sample})
    assert is_grounded(generate_report(cluster, context, "QAL-0001", FakeProvider()))
    bad = generate_report(cluster, context, "QAL-0001", FakeProvider(overrides=overrides))
    assert bad.generator.method == "llm" and not is_grounded(bad), bad.review_reasons


def test_report_scores_on_a_hand_checked_example() -> None:
    sample = load_sample()
    rid = sample.run.run_id
    truth = _truth_for_sample()
    bench = Benchmark(Path("bench"), {rid: sample}, truth)
    doors = make_cluster(sample, SB01_DOORS)  # SB01, feature Doors, expected S2
    combat = make_cluster(sample, SB14_COMBAT)  # SB14, feature Combat math, expected S3
    noise = make_cluster(sample, [34])  # an info log: no seeded bug
    good = _report(
        signature=doors.signature,
        severity="S2",
        component="SeededDoor (Door_02)",
        steps_to_reproduce=[
            {"text": "walk", "source": "bot_log", "action_refs": [{"run_id": rid, "seq": 7}]}
        ],
        generator={
            "method": "llm", "provider": "fake", "latency_ms": 100.0,
            "tokens_in": 100, "tokens_out": 50, "cached": False,
        },
    )  # fmt: skip
    fallback = _report(
        signature=combat.signature,
        severity="S1",
        component="Combat",
        expected="unknown",
        suspected_cause="unknown",
        generator={
            "method": "template", "provider": "fake", "attempts": 3, "latency_ms": 900.0,
            "tokens_in": 900, "tokens_out": 0, "cached": False,
        },
    )  # fmt: skip
    cached = _report(
        signature=noise.signature,
        generator={
            "method": "llm", "provider": "fake", "latency_ms": 5.0,
            "tokens_in": 20, "tokens_out": 10, "cached": True,
        },
    )  # fmt: skip
    score = score_reports(
        "toy",
        [good, fallback, cached],
        [doors, combat, noise],
        bench,
        feature_components(DESIGN_DOC.read_text(encoding="utf-8")),
        {doors.signature: ["Doors", "Spawner"], combat.signature: ["Audio"]},
        k=3,
        provider=None,
    )
    assert (score.reports, score.labelled_reports, score.attempted) == (3, 2, 3)
    assert score.fallback_rate == pytest.approx(1 / 3, abs=1e-4)
    assert score.field_completeness == pytest.approx((1.0 + 5 / 7) / 2, abs=1e-4)
    assert score.grounding_rate == pytest.approx(2 / 3, abs=1e-4), "the fallback counts against"
    assert score.repro_step_match == 0.5, "both clusters follow bot actions; only one cites them"
    assert (score.severity_agreement, score.severity_within_one) == (0.5, 0.5), "gaps 0 and 2"
    assert score.component_correct == 0.5, "'Combat' names none of the Combat math components"
    assert score.retrieval_hit_at_k == 0.5, "SB01's feature retrieved, SB14's not"
    assert (score.latency_p50_ms, score.latency_p95_ms) == (500.0, 860.0), "cached call left out"
    assert score.tokens_per_report == pytest.approx((150 + 900 + 30) / 3)
    assert score.rag is True


def test_hit_rate_counts_a_split_bug_once() -> None:
    sample = load_sample()
    truth = _truth_for_sample()
    halves = [make_cluster(sample, [30]), make_cluster(sample, [31])]  # SB14 split in two
    reports = [_report(signature=c.signature) for c in halves]
    retrieved = {halves[0].signature: ["Combat math"], halves[1].signature: ["Audio"]}
    bench = Benchmark(Path("bench"), {sample.run.run_id: sample}, truth)
    score = score_reports("split", reports, halves, bench, {}, retrieved, 3, None)
    assert score.retrieval_hit_at_k == 1.0, "one bug, found through one of its clusters"
    assert score.component_correct is None, "no design doc given: nothing to check against"


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
        seeded_bugs_present=["SB01", "SB09"],
        bugs_with_events=["SB01"],
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
            attempted=3,
            fallback_rate=0.0,
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
    assert "Seeds triggered (labels.json): SB01, SB09. Seeds with matching events: SB01." in text
    row = "| fake | 0.000 | 1.000 | 1.000 | n/a | 0.500 (1.000) | 0.500 | 1.000 | n/a / n/a | 12 |"
    assert row in text
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


@pytest.mark.youwrite
def test_cli_rag_off_still_scores_components_with_a_design_doc(tmp_path: Path) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=1)
    out = tmp_path / "eval"
    args = ["eval", "triage", str(bench), "--variants", "none", "--reports", "--provider", "fake"]
    args += ["--design-doc", str(DESIGN_DOC), "--out", str(out), "--label", "off", "--no-cache"]
    result = runner.invoke(app, args)
    if isinstance(result.exception, NotImplementedError):
        raise result.exception
    assert result.exit_code == 0, result.output
    scores = json.loads((out / "triage_off.json").read_text(encoding="utf-8"))["reports"]
    assert scores["rag"] is False and scores["retrieval_hit_at_k"] is None, "no RAG, no hit@k"
    assert scores["component_correct"] is not None, "the design doc scores, it isn't retrieved"


def test_cli_rejects_unknown_variants(tmp_path: Path) -> None:
    result = runner.invoke(app, ["eval", "triage", str(tmp_path), "--variants", "magic"])
    assert result.exit_code == 2 and "unknown variant" in result.output


def test_labels_become_safe_file_names() -> None:
    assert safe_label("e2 small  model/v1") == "e2_small_model_v1"


def test_frame_embed_is_skipped_without_a_provider_or_when_it_fails(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=1)
    options = TriageOptions(out=tmp_path / "reports", provider="none")
    result = run_triage_eval(bench, ["frame_embed"], options, False, "x", None)
    assert (
        result.clustering == []
        and "needs an embedding provider" in result.skipped_variants["frame_embed"]
    )

    def down(*args: object, **kwargs: object) -> None:
        raise LLMError("ollama /api/embed unreachable")

    monkeypatch.setattr("qalab.eval.triage_eval.make_provider", down)
    options = TriageOptions(out=tmp_path / "reports", provider="ollama")
    result = run_triage_eval(bench, ["frame_embed"], options, False, "x", None)
    assert result.skipped_variants["frame_embed"] == "failed: ollama /api/embed unreachable"


@pytest.mark.youwrite
def test_unlabelled_runs_are_clustered_but_not_scored(tmp_path: Path) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=1, unlabelled=1)
    options = TriageOptions(out=tmp_path / "reports", provider="none")
    result = run_triage_eval(bench, ["frame_tfidf"], options, False, "x", None)
    score = result.clustering[0]
    assert len(result.runs_without_labels) == 1
    assert score.labelled_events < score.labelled_events + score.unlabelled_events
    assert (score.precision, score.recall, score.f1) == (1.0, 1.0, 1.0), "the copy changes nothing"
