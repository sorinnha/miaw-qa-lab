"""``qalab eval triage``: score clustering (E1) and reports (E2, E3) against seeded ground truth.

Spec 02 (M5) and docs/EVAL_RESULTS.md. Every number the command prints comes from functions in
this module and ``metrics.py``; definitions are in the docstrings and in DECISIONS D-028.
"""

from __future__ import annotations

import json
import logging
import re
import time
from collections import Counter, defaultdict
from collections.abc import Mapping, Sequence
from dataclasses import asdict, dataclass, field, replace
from datetime import UTC, datetime
from pathlib import Path
from typing import Any

from qalab import __version__
from qalab.eval.ground_truth import GroundTruth, build_ground_truth, feature_components, load_labels
from qalab.eval.metrics import cluster_count_error, pairwise_prf, percentile, rate
from qalab.io.runs import LoadedRun
from qalab.llm.base import LLMError, LLMProvider
from qalab.llm.factory import make_provider
from qalab.models.bug import BugReport
from qalab.models.labels import SeededBug
from qalab.rag.retrieve import RetrievedChunk, Retriever, build_query
from qalab.triage.cluster import Cluster, Variant
from qalab.triage.pipeline import (
    TriageOptions,
    build_meta,
    load_runs,
    make_reports,
    make_retriever_for,
    triage_clusters,
    write_outputs,
)
from qalab.triage.prompts import load_prompt

log = logging.getLogger(__name__)

MANIFEST_FILE = "manifest.json"
# A label becomes part of file names: "e2 small model" → "e2_small_model".
_UNSAFE_IN_NAMES = re.compile(r"[^A-Za-z0-9_.-]+")
# Substrings of the review reasons that report_llm.ground_draft writes for grounding failures.
GROUNDING_REASONS = (
    "unknown evidence ids",
    "no valid evidence ids",
    "unknown action ids",
    "unknown doc ids",
)
NARRATIVE_FIELDS = ("title", "component", "summary", "expected", "actual", "suspected_cause")


@dataclass
class Benchmark:
    """The runs of a benchmark folder, their ground truth and the optional manifest."""

    root: Path
    runs: dict[str, LoadedRun]
    truth: GroundTruth
    manifest: dict[str, Any] | None = None


def load_benchmark(root: Path) -> Benchmark:
    """Every run folder under ``root`` plus its ``labels.json``. Runs without labels are kept for
    clustering but contribute no ground truth (they are listed in the output)."""
    root = Path(root)
    runs = load_runs([root])
    labels = {run_id: load_labels(loaded.run_dir) for run_id, loaded in runs.items()}
    if not any(labels.values()):
        raise ValueError(
            f"no labels.json in any run under {root}: record benchmark runs with -qalabBenchmark"
        )
    truth = build_ground_truth({rid: r.events for rid, r in runs.items()}, labels)
    manifest = None
    if (root / MANIFEST_FILE).is_file():
        manifest = json.loads((root / MANIFEST_FILE).read_text(encoding="utf-8"))
    return Benchmark(root, runs, truth, manifest)


# ---- E1: clustering ----------------------------------------------------------------------------


@dataclass
class ClusteringScore:
    variant: str
    precision: float
    recall: float
    f1: float
    predicted_clusters: int  # clusters holding at least one labelled event
    true_bugs: int  # distinct seeded bugs among the labelled events
    cluster_count_error: int  # predicted − true: > 0 split bugs, < 0 merged bugs
    labelled_events: int  # candidate events that match exactly one seeded bug
    # Candidate events matching no seed (noise, or a bug the sandbox didn't seed).
    unlabelled_events: int
    # Bug → number of clusters it was split into (only bugs split into more than one).
    split_bugs: dict[str, int] = field(default_factory=dict)
    # Clusters holding more than one bug, with the event count of each bug.
    mixed_clusters: list[dict[str, Any]] = field(default_factory=list)
    seconds: float = 0.0


def score_clusters(
    clusters: Sequence[Cluster], truth: GroundTruth, variant: str
) -> ClusteringScore:
    """Pairwise P/R/F1 over the clustered events that have ground truth (spec 02 §5, E1)."""
    true_labels: list[str] = []
    pred_labels: list[str] = []
    unlabelled = 0
    bugs_in_cluster: dict[str, Counter[str]] = defaultdict(Counter)
    for cluster in clusters:
        for member in cluster.members:
            bug = truth.bug_of(member.event)
            if bug is None:
                unlabelled += 1
                continue
            true_labels.append(bug)
            pred_labels.append(cluster.signature)
            bugs_in_cluster[cluster.signature][bug] += 1
    precision, recall, f1 = pairwise_prf(true_labels, pred_labels)
    clusters_of_bug: dict[str, set[str]] = defaultdict(set)
    for signature, bugs in bugs_in_cluster.items():
        for bug in bugs:
            clusters_of_bug[bug].add(signature)
    predicted = len(bugs_in_cluster)
    true = len(clusters_of_bug)
    return ClusteringScore(
        variant=variant,
        precision=round(precision, 4),
        recall=round(recall, 4),
        f1=round(f1, 4),
        predicted_clusters=predicted,
        true_bugs=true,
        cluster_count_error=cluster_count_error(predicted, true),
        labelled_events=len(true_labels),
        unlabelled_events=unlabelled,
        split_bugs={b: len(s) for b, s in sorted(clusters_of_bug.items()) if len(s) > 1},
        mixed_clusters=[
            {"signature": sig, "bugs": dict(sorted(bugs.items()))}
            for sig, bugs in sorted(bugs_in_cluster.items())
            if len(bugs) > 1
        ],
    )


def evaluate_clustering(
    bench: Benchmark, variant: Variant, options: TriageOptions, provider: LLMProvider | None
) -> ClusteringScore:
    started = time.perf_counter()
    clusters = triage_clusters(bench.runs, _with_variant(options, variant), provider)
    score = score_clusters(clusters, bench.truth, variant)
    score.seconds = round(time.perf_counter() - started, 2)
    return score


# ---- E2 / E3: reports ----------------------------------------------------------------------------

# What the template writes when it can't know a field: the field is present but says nothing.
PLACEHOLDERS = {"", "unknown", "n/a", "none", "tbd"}


@dataclass
class ReportScore:
    label: str
    provider: str | None
    model: str | None
    rag: bool
    reports: int
    labelled_reports: int  # reports whose cluster is mostly one seeded bug
    attempted: int  # reports the model was asked for: LLM drafts plus template fallbacks
    fallback_rate: float | None  # share of attempted reports that fell back to the template
    field_completeness: float | None
    grounding_rate: float | None
    repro_step_match: float | None
    severity_agreement: float | None
    severity_within_one: float | None
    component_correct: float | None
    retrieval_hit_at_k: float | None
    k: int
    latency_p50_ms: float | None
    latency_p95_ms: float | None
    tokens_per_report: float | None
    seconds: float = 0.0


@dataclass
class LabelledReport:
    """A report whose cluster is mostly one seeded bug, with that cluster and the bug's entry."""

    report: BugReport
    cluster: Cluster
    bug: SeededBug


class RecordingRetriever:
    """Passes each search to the real retriever and keeps the result, so hit@k scores the docs
    the reports were given, without searching a second time."""

    def __init__(self, inner: Retriever) -> None:
        self.inner = inner
        self.results: dict[str, list[RetrievedChunk]] = {}

    def retrieve(self, query: str) -> list[RetrievedChunk]:
        chunks = self.inner.retrieve(query)
        self.results[query] = chunks
        return chunks


def majority_bug(cluster: Cluster, truth: GroundTruth) -> str | None:
    """The seeded bug that more than half of the cluster's labelled members belong to, else None."""
    bugs: Counter[str] = Counter()
    for member in cluster.members:
        bug = truth.bug_of(member.event)
        if bug is not None:
            bugs[bug] += 1
    if not bugs:
        return None
    bug, n = bugs.most_common(1)[0]
    return bug if n * 2 > sum(bugs.values()) else None


def labelled_reports(
    reports: Sequence[BugReport], clusters: Sequence[Cluster], truth: GroundTruth
) -> list[LabelledReport]:
    """The reports that can be checked against the catalog (their cluster is mostly one bug)."""
    by_signature = {c.signature: c for c in clusters}
    labelled: list[LabelledReport] = []
    for report in reports:
        cluster = by_signature.get(report.signature)
        bug = majority_bug(cluster, truth) if cluster is not None else None
        if cluster is not None and bug is not None:
            labelled.append(LabelledReport(report, cluster, truth.bugs[bug]))
    return labelled


def is_filled(value: object) -> bool:
    """False for blank text and for the template's placeholders such as "unknown"."""
    return str(value).strip().lower() not in PLACEHOLDERS


def field_completeness(report: BugReport) -> float:
    """Share of the narrative fields with real content, plus ≥ 1 reproduction step."""
    filled = sum(1 for name in NARRATIVE_FIELDS if is_filled(getattr(report, name)))
    filled += 1 if report.steps_to_reproduce else 0
    return filled / (len(NARRATIVE_FIELDS) + 1)


def was_attempted(report: BugReport) -> bool:
    """The model was asked for this report: an LLM draft, or a template after the drafts failed."""
    return report.generator.provider is not None


def is_grounded(report: BugReport) -> bool:
    """An LLM draft (not a template fallback) with no grounding failure in its review reasons."""
    if report.generator.method != "llm":
        return False
    reasons = " ".join(report.review_reasons or []).lower()
    return not any(r in reasons for r in GROUNDING_REASONS)


def has_bot_step(report: BugReport, action_keys: set[tuple[str, int]]) -> bool:
    """At least one ``bot_log`` step whose action refs all point at real action events."""
    for step in report.steps_to_reproduce:
        refs = step.action_refs or []
        if (
            step.source == "bot_log"
            and refs
            and all((r.run_id, r.seq) in action_keys for r in refs)
        ):
            return True
    return False


def actions_before_first(cluster: Cluster, runs: Mapping[str, LoadedRun]) -> bool:
    """True if the bot logged an action before the cluster's first occurrence in that run."""
    first = cluster.first.event
    return any(e.kind == "action" and e.seq < first.seq for e in runs[first.run_id].events)


def component_matches(report_component: str, names: Sequence[str]) -> bool:
    """The report's component text names one of the feature's components (case-insensitive).
    One way only: "SeededDoor (Door_02)" names SeededDoor; "door" alone doesn't."""
    text = report_component.lower()
    return any(n.lower() in text for n in names)


def severity_gap(item: LabelledReport) -> int:
    """Levels between the report's severity and the catalog's (S2 vs S3 → 1)."""
    return abs(int(item.report.severity[1]) - int(item.bug.expected_severity[1]))


def repro_step_match(
    labelled: Sequence[LabelledReport], runs: Mapping[str, LoadedRun]
) -> float | None:
    """Among clusters with bot actions before their first occurrence, the share of reports with a
    bot step that cites real actions."""
    action_keys = {(e.run_id, e.seq) for r in runs.values() for e in r.events if e.kind == "action"}
    eligible = [item.report for item in labelled if actions_before_first(item.cluster, runs)]
    return rate(sum(1 for r in eligible if has_bot_step(r, action_keys)), len(eligible))


def component_correct(
    labelled: Sequence[LabelledReport], components: Mapping[str, Sequence[str]]
) -> float | None:
    """Share of reports naming one of their feature's components (features without a
    ``Component:`` line in the design doc are left out)."""
    checked = [
        (item.report.component, components[item.bug.feature])
        for item in labelled
        if components.get(item.bug.feature)
    ]
    return rate(sum(1 for text, names in checked if component_matches(text, names)), len(checked))


def retrieval_hit_rate(
    labelled: Sequence[LabelledReport], retrieved_headings: Mapping[str, list[str]]
) -> float | None:
    """Spec 02 §10: share of seeded bugs whose feature heading is among the top-k chunk headings
    retrieved for one of their clusters (a bug split into two clusters still counts once)."""
    found: dict[str, bool] = {}
    for item in labelled:
        hit = item.bug.feature in retrieved_headings.get(item.cluster.signature, [])
        found[item.bug.bug_id] = found.get(item.bug.bug_id, False) or hit
    return rate(sum(found.values()), len(found))


def _mean(values: Sequence[float]) -> float | None:
    return round(sum(values) / len(values), 4) if values else None


def _r4(value: float | None) -> float | None:
    return round(value, 4) if value is not None else None


def score_reports(
    label: str,
    reports: Sequence[BugReport],
    clusters: Sequence[Cluster],
    bench: Benchmark,
    components: Mapping[str, Sequence[str]],
    retrieved_headings: Mapping[str, list[str]] | None,
    k: int,
    provider: LLMProvider | None,
) -> ReportScore:
    """Report quality (E2, E3; definitions in DECISIONS D-028).

    Content metrics (completeness, repro steps, severity, component, hit@k) use the labelled
    reports. Cost metrics (fallback, grounding, latency, tokens) use every report the model was
    asked for, so a model whose drafts fail is not flattered by leaving its failures out.
    """
    labelled = labelled_reports(reports, clusters, bench.truth)
    attempted = [r for r in reports if was_attempted(r)]
    timed = [
        r.generator.latency_ms
        for r in attempted
        if not r.generator.cached and r.generator.latency_ms is not None
    ]
    tokens = [
        float((r.generator.tokens_in or 0) + (r.generator.tokens_out or 0)) for r in attempted
    ]
    gaps = [severity_gap(item) for item in labelled]
    fallbacks = sum(1 for r in attempted if r.generator.method != "llm")
    return ReportScore(
        label=label,
        provider=provider.name if provider else None,
        model=provider.model if provider else None,
        rag=retrieved_headings is not None,
        reports=len(reports),
        labelled_reports=len(labelled),
        attempted=len(attempted),
        fallback_rate=_r4(rate(fallbacks, len(attempted))),
        field_completeness=_mean([field_completeness(item.report) for item in labelled]),
        grounding_rate=_r4(rate(sum(1 for r in attempted if is_grounded(r)), len(attempted))),
        repro_step_match=_r4(repro_step_match(labelled, bench.runs)),
        severity_agreement=_r4(rate(sum(1 for g in gaps if g == 0), len(gaps))),
        severity_within_one=_r4(rate(sum(1 for g in gaps if g <= 1), len(gaps))),
        component_correct=_r4(component_correct(labelled, components)),
        retrieval_hit_at_k=(
            _r4(retrieval_hit_rate(labelled, retrieved_headings))
            if retrieved_headings is not None
            else None
        ),
        k=k,
        latency_p50_ms=_r4(percentile(timed, 50)),
        latency_p95_ms=_r4(percentile(timed, 95)),
        tokens_per_report=_mean(tokens),
    )


def evaluate_reports(
    bench: Benchmark, options: TriageOptions, label: str, design_doc: Path | None
) -> ReportScore:
    """Run the full triage (written to ``options.out`` for inspection) and score its reports."""
    started = time.perf_counter()
    provider = make_provider(
        options.provider,
        options.model,
        options.embed_model,
        use_cache=options.use_cache,
        prompt_version=load_prompt("triage_v1").version,
    )
    clusters = triage_clusters(bench.runs, options, provider)
    retriever = make_retriever_for(options, provider)
    recorder = RecordingRetriever(retriever) if retriever is not None else None
    reports = make_reports(clusters, bench.runs, options, provider, recorder)
    meta = build_meta(bench.runs, clusters, reports, options, provider, {})
    write_outputs(options.out, reports, clusters, bench.runs, meta)

    headings: dict[str, list[str]] | None = None
    if recorder is not None:
        headings = {
            c.signature: [chunk.heading for chunk in recorder.results.get(build_query(c), [])]
            for c in clusters[: options.max_reports]
        }
    components = feature_components(design_doc.read_text(encoding="utf-8")) if design_doc else {}
    score = score_reports(
        label, reports, clusters, bench, components, headings, options.config.rag.top_k, provider
    )
    score.seconds = round(time.perf_counter() - started, 2)
    return score


# ---- the command -------------------------------------------------------------------------------


@dataclass
class EvalResult:
    benchmark: str
    created_at: str
    qalab_version: str
    runs: int
    runs_without_labels: list[str]
    seeded_bugs_present: list[str]  # seeds labels.json says fired (visual ones included)
    bugs_with_events: list[str]  # seeds at least one event matched: what triage could find
    ambiguous_events: int
    manifest: dict[str, Any] | None
    clustering: list[ClusteringScore] = field(default_factory=list)
    skipped_variants: dict[str, str] = field(default_factory=dict)
    reports: ReportScore | None = None

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def run_triage_eval(
    benchmark_dir: Path,
    variants: Sequence[Variant],
    options: TriageOptions,
    evaluate_report_quality: bool,
    label: str,
    design_doc: Path | None,
) -> EvalResult:
    bench = load_benchmark(benchmark_dir)
    result = EvalResult(
        benchmark=str(benchmark_dir),
        created_at=datetime.now(UTC).strftime("%Y-%m-%dT%H:%M:%SZ"),
        qalab_version=__version__,
        runs=len(bench.runs),
        runs_without_labels=bench.truth.runs_without_labels,
        seeded_bugs_present=sorted(bench.truth.bugs),
        bugs_with_events=sorted(set(bench.truth.event_bug.values())),
        ambiguous_events=len(bench.truth.ambiguous),
        manifest=bench.manifest,
    )
    embed_provider: LLMProvider | None = None
    for variant in variants:
        if variant == "frame_embed" and options.provider == "none":
            result.skipped_variants[variant] = (
                "needs an embedding provider (--provider none has none)"
            )
            continue
        try:
            if variant == "frame_embed":
                embed_provider = embed_provider or make_provider(
                    options.provider,
                    options.model,
                    options.embed_model,
                    use_cache=options.use_cache,
                )
            result.clustering.append(
                evaluate_clustering(
                    bench, variant, options, embed_provider if variant == "frame_embed" else None
                )
            )
        except LLMError as exc:
            # A provider that is down costs this variant only; the others are still written.
            result.skipped_variants[variant] = f"failed: {exc}"
    if evaluate_report_quality:
        result.reports = evaluate_reports(bench, options, label, design_doc)
    return result


def _with_variant(options: TriageOptions, variant: Variant) -> TriageOptions:
    return replace(options, variant=variant)


def _fmt(value: float | int | None, digits: int = 3) -> str:
    if value is None:
        return "n/a"
    if isinstance(value, int):
        return str(value)
    return f"{value:.{digits}f}"


def to_markdown(result: EvalResult) -> str:
    """The tables of docs/EVAL_RESULTS.md, filled from this result (paste them as they are)."""
    present = ", ".join(result.seeded_bugs_present) or "none"
    with_events = ", ".join(result.bugs_with_events) or "none"
    lines = [
        f"Benchmark `{result.benchmark}`: {result.runs} runs. Seeds triggered (labels.json): "
        f"{present}. Seeds with matching events: {with_events}. "
        f"Created {result.created_at}, qalab {result.qalab_version}.",
        "",
    ]
    if result.clustering or result.skipped_variants:
        lines += [
            "| Variant | Pairwise P | Pairwise R | F1 | Clusters / true bugs | Notes |",
            "|---|---|---|---|---|---|",
        ]
        for s in result.clustering:
            notes = []
            if s.split_bugs:
                notes.append("split: " + ", ".join(f"{b}×{n}" for b, n in s.split_bugs.items()))
            if s.mixed_clusters:
                notes.append("merged: " + "; ".join("+".join(m["bugs"]) for m in s.mixed_clusters))
            lines.append(
                f"| {s.variant} | {_fmt(s.precision)} | {_fmt(s.recall)} | {_fmt(s.f1)} | "
                f"{s.predicted_clusters} / {s.true_bugs} | {'; '.join(notes) or '-'} |"
            )
        for variant, reason in result.skipped_variants.items():
            lines.append(f"| {variant} | skipped: {reason} | | | | |")
        lines.append("")
    if result.reports is not None:
        r = result.reports
        rag = "on" if r.rag else "off"
        cells = [
            r.label,
            _fmt(r.fallback_rate),
            _fmt(r.field_completeness),
            _fmt(r.grounding_rate),
            _fmt(r.repro_step_match),
            f"{_fmt(r.severity_agreement)} ({_fmt(r.severity_within_one)})",
            _fmt(r.component_correct),
            _fmt(r.retrieval_hit_at_k),
            f"{_fmt(r.latency_p50_ms, 0)} / {_fmt(r.latency_p95_ms, 0)}",
            _fmt(r.tokens_per_report, 0),
        ]
        header = [
            "Setting",
            "Fallback rate",
            "Field completeness",
            "Grounding rate",
            "Repro-step match",
            "Severity agreement (±1)",
            "Component correct",
            f"Retrieval hit@{r.k}",
            "Latency p50 / p95 ms",
            "Tokens per report",
        ]
        lines += [
            f"Reports `{r.label}`: provider {r.provider or 'none'}, model {r.model or '-'}, "
            f"RAG {rag}; {r.labelled_reports} of {r.reports} reports map to a seeded bug; "
            f"the model was asked for {r.attempted}.",
            "",
            "| " + " | ".join(header) + " |",
            "|" + "---|" * len(header),
            "| " + " | ".join(cells) + " |",
            "",
        ]
    return "\n".join(lines)


def write_charts(result: EvalResult, out: Path, stem: str) -> list[Path]:
    """E1 grouped bars (P/R/F1 per variant) and, with reports, the report-quality bars."""
    import matplotlib

    matplotlib.use("Agg")
    import matplotlib.pyplot as plt

    written: list[Path] = []
    if result.clustering:
        fig, ax = plt.subplots(figsize=(7, 3.6))
        names = [s.variant for s in result.clustering]
        width = 0.26
        for i, (metric, title) in enumerate(
            (("precision", "Precision"), ("recall", "Recall"), ("f1", "F1"))
        ):
            values = [getattr(s, metric) for s in result.clustering]
            ax.bar([x + (i - 1) * width for x in range(len(names))], values, width, label=title)
        ax.set_xticks(range(len(names)), names)
        ax.set_ylim(0, 1.05)
        ax.set_ylabel("pairwise score")
        ax.set_title("E1: clustering variants")
        ax.legend(loc="lower right", frameon=False)
        ax.spines[["top", "right"]].set_visible(False)
        fig.tight_layout()
        path = out / f"{stem}_e1_prf.png"
        fig.savefig(path, dpi=150)
        plt.close(fig)
        written.append(path)
    if result.reports is not None:
        r = result.reports
        metrics = [
            ("Field\ncompleteness", r.field_completeness),
            ("Grounding", r.grounding_rate),
            ("Repro-step\nmatch", r.repro_step_match),
            ("Severity\nagreement", r.severity_agreement),
            ("Component\ncorrect", r.component_correct),
            (f"Retrieval\nhit@{r.k}", r.retrieval_hit_at_k),
        ]
        shown = [(name, v) for name, v in metrics if v is not None]
        fig, ax = plt.subplots(figsize=(7, 3.6))
        ax.bar([n for n, _ in shown], [v for _, v in shown], color="#4C78A8")
        ax.set_ylim(0, 1.05)
        ax.set_title(f"Report quality: {r.label}")
        ax.spines[["top", "right"]].set_visible(False)
        fig.tight_layout()
        path = out / f"{stem}_reports.png"
        fig.savefig(path, dpi=150)
        plt.close(fig)
        written.append(path)
    return written


def safe_label(label: str) -> str:
    """The label as used in output names (``triage_<label>.json``, ``reports_<label>/``)."""
    return _UNSAFE_IN_NAMES.sub("_", label)


def write_result(result: EvalResult, out: Path, label: str) -> list[Path]:
    """``triage_<label>.json`` (everything), ``.md`` (the tables) and the charts, in ``out``."""
    out.mkdir(parents=True, exist_ok=True)
    stem = "triage_" + safe_label(label)
    json_path = out / f"{stem}.json"
    json_path.write_text(json.dumps(result.to_dict(), indent=2) + "\n", encoding="utf-8")
    md_path = out / f"{stem}.md"
    md_path.write_text(to_markdown(result) + "\n", encoding="utf-8")
    return [json_path, md_path, *write_charts(result, out, stem)]
