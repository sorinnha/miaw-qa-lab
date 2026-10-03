from pathlib import Path

import numpy as np
import pytest

from qalab.io.runs import LoadedRun
from qalab.llm.fake import FakeProvider
from qalab.rag.chunk import Chunk, chunk_file
from qalab.rag.index import DocIndex, build_index
from qalab.rag.retrieve import (
    EmbeddingRetriever,
    TfidfRetriever,
    build_query,
    cosine_top_k,
    make_retriever,
)
from tests.triage.fixtures import (
    SB01_DOORS,
    SB02_INVENTORY,
    SB03_ENEMY_REGISTRY,
    SB06_FELL,
    load_sample,
    make_cluster,
)

REPO = Path(__file__).resolve().parents[3]
DESIGN_DOC = REPO / "docs" / "sandbox_design.md"


@pytest.fixture(scope="module")
def chunks() -> list[Chunk]:
    return chunk_file(DESIGN_DOC)


@pytest.fixture(scope="module")
def loaded() -> LoadedRun:
    return load_sample()


def test_build_query(loaded: LoadedRun) -> None:
    sb03 = make_cluster(loaded, SB03_ENEMY_REGISTRY, normalized_message="The given key <str>")
    assert build_query(sb03) == (
        "KeyNotFoundException The given key <str> "
        "QALab.Sandbox.SeededEnemyRegistry.Get Sandbox_Level01"
    )
    fell = make_cluster(loaded, SB06_FELL)
    assert build_query(fell) == "Sandbox_Level01 fell_out_of_world"


def test_tfidf_retriever_finds_the_feature_heading(chunks: list[Chunk], loaded: LoadedRun) -> None:
    retriever = TfidfRetriever(chunks, top_k=3, min_score=0.0)
    expected = {
        "Doors": make_cluster(loaded, SB01_DOORS, normalized_message="Object reference not set"),
        "Inventory": make_cluster(
            loaded, SB02_INVENTORY, normalized_message="Inventory slot <n> out of range"
        ),
        "Enemy registry": make_cluster(
            loaded, SB03_ENEMY_REGISTRY, normalized_message="The given key <str> was not present"
        ),
    }
    for heading, cluster in expected.items():
        hits = retriever.retrieve(build_query(cluster))
        assert hits and heading in [h.heading for h in hits], heading
        assert hits[0].score >= hits[-1].score
        assert hits[0].chunk_id.startswith("sandbox_design.md#")
    assert retriever.retrieve("") == []
    assert TfidfRetriever([]).retrieve("doors") == []


def test_min_score_filters_weak_hits(chunks: list[Chunk]) -> None:
    assert TfidfRetriever(chunks, min_score=0.99).retrieve("door opens") == []
    assert len(TfidfRetriever(chunks, top_k=2, min_score=0.0).retrieve("door opens")) == 2


def test_make_retriever_picks_tfidf_without_provider(chunks: list[Chunk], tmp_path: Path) -> None:
    index = build_index([DESIGN_DOC], FakeProvider(), index_dir=tmp_path)
    assert isinstance(make_retriever(index, None), TfidfRetriever)
    assert isinstance(make_retriever(index, FakeProvider()), EmbeddingRetriever)
    assert isinstance(make_retriever(DocIndex(chunks=chunks), FakeProvider()), TfidfRetriever)


# ---- YOU WRITE: cosine_top_k and everything that calls it ---------------------------------


@pytest.mark.youwrite
def test_cosine_top_k_orders_and_limits() -> None:
    matrix = np.array([[1.0, 0.0], [0.0, 1.0], [0.6, 0.8], [-1.0, 0.0]], dtype=np.float32)
    query = np.array([1.0, 0.0], dtype=np.float32)
    hits = cosine_top_k(query, matrix, k=3)
    assert [i for i, _ in hits] == [0, 2, 1]
    assert [round(s, 3) for _, s in hits] == [1.0, 0.6, 0.0]
    assert all(isinstance(i, int) and isinstance(s, float) for i, s in hits)
    assert len(cosine_top_k(query, matrix, k=10)) == 4
    assert cosine_top_k(query, np.zeros((0, 2), dtype=np.float32), k=3) == []


@pytest.mark.youwrite
def test_embedding_retriever_on_fake_index(loaded: LoadedRun, tmp_path: Path) -> None:
    provider = FakeProvider()
    index = build_index([DESIGN_DOC], provider, index_dir=tmp_path)
    retriever = EmbeddingRetriever(index, provider, top_k=3, min_score=0.0)
    hits = retriever.retrieve("door opens Interact SeededDoor Interactor creak hinge")
    assert len(hits) == 3 and hits[0].heading == "Doors"
    assert hits[0].score >= hits[1].score >= hits[2].score
    assert retriever.retrieve("") == []
    assert EmbeddingRetriever(index, provider, min_score=1.01).retrieve("door") == []
