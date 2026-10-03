import json
import shutil
from pathlib import Path

from qalab.config import RankConfig
from qalab.io.runs import load_run
from qalab.triage.rank import rank_clusters, score_cluster
from tests.triage.fixtures import (
    SAMPLE_RUN,
    SB01_DOORS,
    SB03_ENEMY_REGISTRY,
    SB06_FELL,
    SB08_PERF,
    SB13_AUDIO,
    load_sample,
    make_cluster,
)


def test_formula_and_breakdown() -> None:
    loaded = load_sample()
    runs = {loaded.run.run_id: loaded}
    sb03 = score_cluster(make_cluster(loaded, SB03_ENEMY_REGISTRY), runs)
    assert sb03.score == 10.0 and sb03.priority == "P2"  # 5 × (1 + log2 2)
    assert sb03.score_breakdown["weight"] == 5.0 and sb03.score_breakdown["crash_factor"] == 1.0
    sb01 = score_cluster(make_cluster(loaded, SB01_DOORS), runs)
    assert sb01.score == 5.0 and sb01.priority == "P3"
    sb06 = score_cluster(make_cluster(loaded, SB06_FELL), runs)
    assert sb06.score == 5.0  # critical detector
    sb13 = score_cluster(make_cluster(loaded, SB13_AUDIO), runs)
    assert round(sb13.score, 3) == 2.585 and sb13.priority == "P4"


def test_blocker_is_always_p1() -> None:
    loaded = load_sample()
    runs = {loaded.run.run_id: loaded}
    blocker = make_cluster(loaded, SB08_PERF, detector_severity="blocker")
    assert score_cluster(blocker, runs).priority == "P1"


def test_warning_spam_ranks_in_the_lowest_band() -> None:
    loaded = load_sample()
    runs = {loaded.run.run_id: loaded}
    clusters = [
        make_cluster(loaded, seqs)
        for seqs in (SB01_DOORS, SB03_ENEMY_REGISTRY, SB06_FELL, SB08_PERF, SB13_AUDIO)
    ]
    ranked = rank_clusters(clusters, runs)
    assert ranked[0].exception_type == "KeyNotFoundException"
    sb13 = next(c for c in ranked if c.level == "warning")
    assert sb13.priority == "P4"
    assert all(c.score > sb13.score for c in ranked if c.kind == "log" and c.level != "warning")
    # Spec §6 formula vs EXPECTED.md: a single minor detector (2.0) scores below 3 warnings
    # (1 × (1 + log2 3) ≈ 2.58), so SB13 is second to last, not last. Logged in DECISIONS D-012.
    assert ranked[-1].detector == "perf_spike" and ranked[-2] is sb13


def test_crash_multiplier_and_runs_factor(tmp_path: Path) -> None:
    crashed_dir = tmp_path / "crashed"
    shutil.copytree(SAMPLE_RUN, crashed_dir, ignore=shutil.ignore_patterns("shots", "labels.json"))
    raw = json.loads((crashed_dir / "run.json").read_text(encoding="utf-8"))
    del raw["ended_at"]
    raw["run_id"] = "crashed-run"
    (crashed_dir / "run.json").write_text(json.dumps(raw), encoding="utf-8")
    events_path = crashed_dir / "events.jsonl"
    events_path.write_text(
        events_path.read_text(encoding="utf-8").replace("20261005T103000Z-s42", "crashed-run"),
        encoding="utf-8",
    )
    crashed = load_run(crashed_dir)
    loaded = load_sample()
    runs = {loaded.run.run_id: loaded, crashed.run.run_id: crashed}

    only_crashed = score_cluster(make_cluster(crashed, SB01_DOORS), runs)
    assert only_crashed.score == 6.25 and only_crashed.score_breakdown["crash_factor"] == 1.25

    both = make_cluster(loaded, SB01_DOORS)
    both.members.extend(make_cluster(crashed, SB01_DOORS).members)
    both = score_cluster(both, runs)
    # 5 × (1 + log2 2) × (1 + 0.5) × 1.25 = 18.75 → P1
    assert both.score == 18.75 and both.priority == "P1"
    assert both.score_breakdown["runs_factor"] == 1.5

    strict = score_cluster(make_cluster(loaded, SB01_DOORS), runs, RankConfig(p3=6.0))
    assert strict.priority == "P4"
