"""Spec 02: the whole ``qalab triage run`` flow, from run folders to ``<out>/``.

Steps: load → cluster (§4–5) → rank (§6) → context (§7, with RAG docs §10 and code) →
report (§8, LLM or template) → write outputs (§11). ``labels.json`` is never read.
"""

from __future__ import annotations

import logging
import time
from collections.abc import Mapping, Sequence
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

from qalab import __version__
from qalab.config import Config
from qalab.io.runs import LoadedRun, discover_runs, load_run
from qalab.io.writers import (
    copy_attachments,
    write_bugs_json,
    write_clusters_json,
    write_json,
    write_text,
)
from qalab.llm.base import LLMProvider
from qalab.llm.cache import CachedProvider
from qalab.llm.factory import make_provider
from qalab.models.bug import BugReport
from qalab.rag.code_context import code_context_for
from qalab.rag.index import DocIndex, build_index, chunk_only_index
from qalab.rag.retrieve import Retriever, build_query, make_retriever
from qalab.report.html import render_html
from qalab.report.jira_csv import write_jira_csv
from qalab.report.markdown import render_markdown
from qalab.triage.cluster import Cluster, Variant, build_clusters
from qalab.triage.context import build_context
from qalab.triage.prompts import load_prompt
from qalab.triage.rank import rank_clusters
from qalab.triage.report_llm import generate_report

log = logging.getLogger(__name__)

EXIT_OK = 0
EXIT_P1 = 3


@dataclass
class TriageOptions:
    out: Path
    provider: str = "fake"
    model: str | None = None
    embed_model: str | None = None
    variant: Variant = "frame_tfidf"
    max_reports: int = 25
    docs: list[Path] = field(default_factory=list)
    repo: Path | None = None
    use_cache: bool = True
    config: Config = field(default_factory=Config)


@dataclass
class TriageResult:
    out: Path
    runs: dict[str, LoadedRun]
    clusters: list[Cluster]
    reports: list[BugReport]
    meta: dict[str, Any]

    @property
    def exit_code(self) -> int:
        return EXIT_P1 if any(r.priority == "P1" for r in self.reports) else EXIT_OK


def load_runs(run_dirs: Sequence[str | Path]) -> dict[str, LoadedRun]:
    found = discover_runs(run_dirs)
    if not found:
        raise FileNotFoundError(f"no run folders under {', '.join(map(str, run_dirs))}")
    runs = [load_run(path) for path in found]
    return {r.run.run_id: r for r in runs}


def triage_clusters(
    runs: Mapping[str, LoadedRun], options: TriageOptions, provider: LLMProvider | None
) -> list[Cluster]:
    """Cluster with the chosen variant, then rank (best first)."""
    triage = options.config.triage
    clusters = build_clusters(
        runs,
        options.variant,
        provider,
        min_level=triage.min_level,
        cell_size_m=triage.cell_size_m,
        dbscan_eps_m=triage.dbscan_eps_m,
        merge_thresholds=vars(triage.merge),
    )
    return rank_clusters(clusters, runs, options.config.rank)


def make_retriever_for(options: TriageOptions, provider: LLMProvider | None) -> Retriever | None:
    if not options.docs:
        return None
    rag = options.config.rag
    if provider is None:
        index: DocIndex = chunk_only_index(options.docs, rag.chunk_chars, rag.overlap_chars)
    else:
        index = build_index(
            options.docs, provider, chunk_chars=rag.chunk_chars, overlap_chars=rag.overlap_chars
        )
    return make_retriever(index, provider, rag.top_k, rag.min_score, rag.tfidf_min_score)


def make_reports(
    clusters: Sequence[Cluster],
    runs: Mapping[str, LoadedRun],
    options: TriageOptions,
    provider: LLMProvider | None,
    retriever: Retriever | None = None,
) -> list[BugReport]:
    """Top ``max_reports`` clusters → reports with ids QAL-0001… in ranked order."""
    reports: list[BugReport] = []
    prompt = load_prompt("triage_v1")
    for number, cluster in enumerate(clusters[: options.max_reports], start=1):
        docs = retriever.retrieve(build_query(cluster)) if retriever else []
        code = code_context_for(cluster.top_frame, options.repo)
        context = build_context(
            cluster, runs, docs=docs, code=code, budget_chars=options.config.triage.context_chars
        )
        report = generate_report(
            cluster,
            context,
            f"QAL-{number:04d}",
            provider,
            max_retries=options.config.llm.max_retries,
            temperature=options.config.llm.temperature,
            prompt=prompt,
        )
        reports.append(copy_attachments(report, cluster, runs, options.out))
    return reports


def write_outputs(
    out: Path,
    reports: Sequence[BugReport],
    clusters: Sequence[Cluster],
    runs: Mapping[str, LoadedRun],
    meta: Mapping[str, Any],
) -> None:
    out.mkdir(parents=True, exist_ok=True)
    write_bugs_json(out / "bugs.json", reports)
    write_text(out / "report.md", render_markdown(reports, meta))
    write_jira_csv(out / "bugs_jira.csv", reports)
    write_text(out / "report.html", render_html(reports, meta))
    write_clusters_json(out / "clusters.json", clusters)
    write_json(out / "validation_report.json", [r.report.to_dict() for r in runs.values()])
    write_json(out / "triage_meta.json", dict(meta))


def build_meta(
    runs: Mapping[str, LoadedRun],
    clusters: Sequence[Cluster],
    reports: Sequence[BugReport],
    options: TriageOptions,
    provider: LLMProvider | None,
    timings_ms: Mapping[str, float],
) -> dict[str, Any]:
    cache_hits = cache_misses = None
    if isinstance(provider, CachedProvider) and provider.cache is not None:
        cache_hits, cache_misses = provider.cache.hits, provider.cache.misses
    return {
        "qalab_version": __version__,
        "run_ids": sorted(runs),
        "variant": options.variant,
        "provider": provider.name if provider else None,
        "model": provider.model if provider else None,
        "prompt_version": load_prompt("triage_v1").version if provider else None,
        "docs": [p.name for p in options.docs],
        "repo": str(options.repo) if options.repo else None,
        "cluster_count": len(clusters),
        "report_count": len(reports),
        "by_priority": {
            p: sum(1 for r in reports if r.priority == p) for p in ("P1", "P2", "P3", "P4")
        },
        "needs_review": sum(1 for r in reports if r.needs_review),
        "tokens_in": sum(r.generator.tokens_in or 0 for r in reports),
        "tokens_out": sum(r.generator.tokens_out or 0 for r in reports),
        "llm_attempts": sum(r.generator.attempts or 0 for r in reports if r.generator.provider),
        "cache_hits": cache_hits,
        "cache_misses": cache_misses,
        "timings_ms": {k: round(v, 1) for k, v in timings_ms.items()},
    }


def run_triage(run_dirs: Sequence[str | Path], options: TriageOptions) -> TriageResult:
    """The full pipeline; returns everything written so callers can inspect it."""
    started = time.perf_counter()
    timings: dict[str, float] = {}
    provider = make_provider(
        options.provider,
        options.model,
        options.embed_model,
        use_cache=options.use_cache,
        prompt_version=load_prompt("triage_v1").version,
    )

    runs = load_runs(run_dirs)
    timings["load"] = (time.perf_counter() - started) * 1000

    mark = time.perf_counter()
    clusters = triage_clusters(runs, options, provider)
    timings["cluster_rank"] = (time.perf_counter() - mark) * 1000

    mark = time.perf_counter()
    retriever = make_retriever_for(options, provider)
    reports = make_reports(clusters, runs, options, provider, retriever)
    timings["reports"] = (time.perf_counter() - mark) * 1000
    timings["total"] = (time.perf_counter() - started) * 1000

    meta = build_meta(runs, clusters, reports, options, provider, timings)
    write_outputs(options.out, reports, clusters, runs, meta)
    log.info("wrote %d reports to %s", len(reports), options.out)
    return TriageResult(options.out, runs, clusters, reports, meta)
