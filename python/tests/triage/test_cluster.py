"""Spec 02 §4–5 on samples/sample_run (see EXPECTED.md). Calls normalize → youwrite."""

import pytest

from qalab.llm.fake import FakeProvider
from qalab.triage.cluster import UnionFind, build_clusters, merge_detector_cells
from tests.triage.fixtures import SB06_FELL, SB08_PERF, load_sample, make_cluster


@pytest.fixture(scope="module")
def runs():
    loaded = load_sample()
    return {loaded.run.run_id: loaded}


def _seqs(clusters) -> list[list[int]]:
    return sorted([m.event.seq for m in c.members] for c in clusters)


def test_union_find() -> None:
    uf = UnionFind(4)
    uf.union(0, 2)
    uf.union(3, 2)
    assert uf.find(3) == uf.find(0) == 0 and uf.find(1) == 1


def test_detector_cells_merge_only_same_detector_and_scene(runs) -> None:
    loaded = next(iter(runs.values()))
    fell = make_cluster(loaded, SB06_FELL, cell=(6, 3))
    perf = make_cluster(loaded, SB08_PERF, cell=(4, 2))
    assert len(merge_detector_cells([fell, perf], eps_m=3.0)) == 2
    # Two fell_out_of_world clusters 2 m apart (different cells) become one.
    near = make_cluster(loaded, SB06_FELL, signature="aaaaaaaaaaaa", cell=(7, 3))
    near.members[0].event = near.members[0].event.model_copy(update={"pos": (28.2, -12.0, 14.1)})
    merged = merge_detector_cells([fell, near], eps_m=3.0)
    assert len(merged) == 1 and merged[0].count == 2
    assert merged[0].signature == min(fell.signature, "aaaaaaaaaaaa")
    assert merged[0].merged_from == [max(fell.signature, "aaaaaaaaaaaa")]


@pytest.mark.youwrite
def test_exact_gives_nine_clusters(runs) -> None:
    clusters = build_clusters(runs, "exact")
    assert len(clusters) == 9
    assert _seqs(clusters) == [[8], [11, 15], [13, 16, 28], [18], [19, 26], [21], [23], [30], [31]]
    assert [c.signature for c in clusters] == sorted(c.signature for c in clusters)
    assert not any("MiawWorks.QALab" in f for c in clusters for f in c.top_frames)


@pytest.mark.youwrite
def test_frame_tfidf_merges_sb14_only(runs) -> None:
    clusters = build_clusters(runs, "frame_tfidf")
    assert len(clusters) == 8
    assert [30, 31] in _seqs(clusters) and [8] in _seqs(clusters) and [18] in _seqs(clusters)
    sb14 = next(
        c
        for c in clusters
        if c.count == 2 and c.level == "error" and 30 in {m.event.seq for m in c.members}
    )
    assert len(sb14.merged_from) == 1


@pytest.mark.youwrite
def test_tfidf_only_wrongly_merges_sb01_and_sb04(runs) -> None:
    clusters = build_clusters(runs, "tfidf_only")
    assert len(clusters) == 8
    assert [8, 18] in _seqs(clusters) and [30] in _seqs(clusters)


@pytest.mark.youwrite
def test_frame_embed_runs_with_fake_embeddings(runs) -> None:
    clusters = build_clusters(runs, "frame_embed", provider=FakeProvider())
    assert 8 <= len(clusters) <= 9
    assert [8, 18] not in _seqs(clusters)  # frame rule keeps SB01 and SB04 apart
    with pytest.raises(ValueError):
        build_clusters(runs, "frame_embed")


@pytest.mark.youwrite
def test_results_are_deterministic(runs) -> None:
    first = build_clusters(runs, "frame_tfidf")
    second = build_clusters(runs, "frame_tfidf")
    assert [c.signature for c in first] == [c.signature for c in second]
