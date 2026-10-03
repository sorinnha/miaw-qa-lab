"""Spec 02 §11 outputs, built from fixture clusters so they don't depend on YOU WRITE code."""

import csv
import json
from pathlib import Path

import pytest

from qalab.config import Config
from qalab.llm.fake import FakeProvider
from qalab.models.bug import BugReport
from qalab.models.schemas import first_error
from qalab.rag.index import chunk_only_index
from qalab.rag.retrieve import make_retriever
from qalab.report.html import rerender_html
from qalab.report.jira_csv import COLUMNS
from qalab.triage.cluster import Cluster
from qalab.triage.pipeline import TriageOptions, build_meta, make_reports, write_outputs
from qalab.triage.rank import rank_clusters
from tests.triage.fixtures import (
    SAMPLE_RUN as SAMPLE_RUN_DIR,
)
from tests.triage.fixtures import (
    SB01_DOORS,
    SB02_INVENTORY,
    SB03_ENEMY_REGISTRY,
    SB06_FELL,
    SB08_PERF,
    SB13_AUDIO,
    SB14_COMBAT,
    load_sample,
    make_cluster,
)

REPO = Path(__file__).resolve().parents[3]
DESIGN_DOC = REPO / "docs" / "sandbox_design.md"
ALL = [
    SB01_DOORS,
    SB02_INVENTORY,
    SB03_ENEMY_REGISTRY,
    SB06_FELL,
    SB08_PERF,
    SB13_AUDIO,
    SB14_COMBAT,
]


@pytest.fixture
def written(tmp_path: Path) -> tuple[Path, list[BugReport], list[Cluster]]:
    loaded = load_sample()
    runs = {loaded.run.run_id: loaded}
    clusters = rank_clusters([make_cluster(loaded, seqs) for seqs in ALL], runs)
    options = TriageOptions(
        out=tmp_path / "out", provider="fake", docs=[DESIGN_DOC], max_reports=25
    )
    provider = FakeProvider()
    # TF-IDF retrieval on purpose: the embedding path needs the YOU WRITE cosine_top_k.
    retriever = make_retriever(chunk_only_index([DESIGN_DOC]), None)
    reports = make_reports(clusters, runs, options, provider, retriever)
    meta = build_meta(runs, clusters, reports, options, provider, {"total": 1.0})
    write_outputs(options.out, reports, clusters, runs, meta)
    return options.out, reports, clusters


def test_bugs_json_validates_and_is_ordered(
    written: tuple[Path, list[BugReport], list[Cluster]],
) -> None:
    out, reports, clusters = written
    data = json.loads((out / "bugs.json").read_text(encoding="utf-8"))
    assert len(data) == 7 and [d["id"] for d in data] == [f"QAL-{i:04d}" for i in range(1, 8)]
    for item in data:
        assert first_error("bug_report", item) is None
    priorities = [d["priority"] for d in data]
    assert priorities == sorted(priorities)
    scores = [d["score"] for d in data]
    for a, b in zip(data, data[1:], strict=False):
        assert (a["priority"], -a["score"]) <= (b["priority"], -b["score"])
    assert scores[0] == 10.0
    # evidence resolves to real events
    seqs = {e.seq for e in load_sample().events}
    assert all(e["seq"] in seqs for d in data for e in d["evidence"])


def test_csv_header_and_rows(written: tuple[Path, list[BugReport], list[Cluster]]) -> None:
    out, reports, _ = written
    with (out / "bugs_jira.csv").open(encoding="utf-8", newline="") as f:
        rows = list(csv.reader(f))
    assert rows[0] == COLUMNS
    assert len(rows) == 1 + len(reports)
    assert rows[1][2] == "Bug" and rows[1][3] == "High" and rows[1][4] == "qalab;log"


def test_html_contains_every_bug_and_is_self_contained(
    written: tuple[Path, list[BugReport], list[Cluster]],
) -> None:
    out, reports, _ = written
    html = (out / "report.html").read_text(encoding="utf-8")
    for r in reports:
        assert r.id in html
    assert "<script src" not in html and "https://" not in html.split("<body>")[0]
    assert 'id="q"' in html and "shots/20261005T103000Z-s42/000003.png" in html
    assert (out / "shots" / "20261005T103000Z-s42" / "000003.png").is_file()
    # re-render from bugs.json gives the same ids again
    (out / "report.html").unlink()
    rerender_html(out)
    assert all(r.id in (out / "report.html").read_text(encoding="utf-8") for r in reports)


def test_markdown_clusters_meta_and_validation(
    written: tuple[Path, list[BugReport], list[Cluster]],
) -> None:
    out, reports, clusters = written
    md = (out / "report.md").read_text(encoding="utf-8")
    assert "| ID | Priority |" in md and all(f"## {r.id}" in md for r in reports)
    clusters_json = json.loads((out / "clusters.json").read_text(encoding="utf-8"))
    assert len(clusters_json) == len(clusters) and clusters_json[0]["members"]
    meta = json.loads((out / "triage_meta.json").read_text(encoding="utf-8"))
    assert meta["provider"] == "fake" and meta["prompt_version"] == "triage-v1"
    assert meta["report_count"] == 7 and meta["tokens_in"] > 0 and "total" in meta["timings_ms"]
    assert meta["by_priority"]["P2"] == 1
    validation = json.loads((out / "validation_report.json").read_text(encoding="utf-8"))
    assert validation[0]["ok"] is True and validation[0]["valid_events"] == 38


def test_template_reports_without_provider(tmp_path: Path) -> None:
    loaded = load_sample()
    runs = {loaded.run.run_id: loaded}
    clusters = rank_clusters([make_cluster(loaded, s) for s in (SB01_DOORS, SB06_FELL)], runs)
    options = TriageOptions(out=tmp_path, provider="none", docs=[DESIGN_DOC], config=Config())
    from qalab.triage.pipeline import make_retriever_for

    reports = make_reports(clusters, runs, options, None, make_retriever_for(options, None))
    assert all(r.generator.method == "template" for r in reports)
    doors = next(r for r in reports if r.kind == "log")
    assert doors.docs_used and "sandbox_design.md#doors-0" in doors.docs_used
    meta = build_meta(runs, clusters, reports, options, None, {})
    write_outputs(tmp_path, reports, clusters, runs, meta)
    assert all(
        first_error("bug_report", d) is None
        for d in json.loads((tmp_path / "bugs.json").read_text())
    )
    assert BugReport.model_validate(json.loads((tmp_path / "bugs.json").read_text())[0])


def test_max_reports_limits_llm_calls(tmp_path: Path) -> None:
    loaded = load_sample()
    runs = {loaded.run.run_id: loaded}
    clusters = rank_clusters([make_cluster(loaded, seqs) for seqs in ALL], runs)
    provider = FakeProvider()
    options = TriageOptions(out=tmp_path, provider="fake", max_reports=2)
    reports = make_reports(clusters, runs, options, provider)
    assert len(reports) == 2 and len(provider.calls) == 2


def test_meta_records_cache_hits(tmp_path: Path) -> None:
    from qalab.llm.factory import make_provider

    loaded = load_sample()
    runs = {loaded.run.run_id: loaded}
    clusters = rank_clusters([make_cluster(loaded, SB01_DOORS)], runs)
    provider = make_provider("fake", cache_path=tmp_path / "llm.sqlite", prompt_version="triage-v1")
    options = TriageOptions(out=tmp_path, provider="fake")
    make_reports(clusters, runs, options, provider)
    reports = make_reports(clusters, runs, options, provider)
    meta = build_meta(runs, clusters, reports, options, provider, {})
    assert meta["cache_hits"] == 1 and meta["cache_misses"] == 1
    assert reports[0].generator.cached is True


def test_duplicate_run_ids_are_rejected(tmp_path: Path) -> None:
    import shutil

    from qalab.triage.pipeline import load_runs

    for name in ("a", "b"):
        shutil.copytree(SAMPLE_RUN_DIR, tmp_path / name, ignore=shutil.ignore_patterns("shots"))
    with pytest.raises(ValueError, match="run ids must be unique"):
        load_runs([tmp_path / "a", tmp_path / "b"])
