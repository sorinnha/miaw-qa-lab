from pathlib import Path

import numpy as np

from qalab.llm.fake import FakeProvider
from qalab.rag.index import build_index, embed_in_batches

REPO = Path(__file__).resolve().parents[3]
DESIGN_DOC = REPO / "docs" / "sandbox_design.md"


def test_embed_in_batches_of_32() -> None:
    provider = FakeProvider()
    matrix = embed_in_batches(provider, [f"text {i}" for i in range(70)], batch_size=32)
    assert matrix.shape == (70, 256) and matrix.dtype == np.float32
    assert [len(call) for call in provider.embed_calls] == [32, 32, 6]
    assert embed_in_batches(provider, []).shape == (0, 0)


def test_index_is_cached_per_file_and_model(tmp_path: Path) -> None:
    provider = FakeProvider()
    index = build_index([DESIGN_DOC], provider, index_dir=tmp_path)
    assert len(index) == index.matrix.shape[0] > 10
    assert index.reembedded_files == ["sandbox_design.md"] and index.model == "fake-1"
    np.testing.assert_allclose(np.linalg.norm(index.matrix, axis=1), 1.0, atol=1e-6)
    assert len(list(tmp_path.glob("*.npz"))) == 1 and len(list(tmp_path.glob("*.json"))) == 1

    again = build_index([DESIGN_DOC], FakeProvider(), index_dir=tmp_path)
    assert again.reembedded_files == [] and len(again) == len(index)
    np.testing.assert_array_equal(again.matrix, index.matrix)
    assert [c.chunk_id for c in again.chunks] == [c.chunk_id for c in index.chunks]

    other_model = build_index([DESIGN_DOC], FakeProvider(model="fake-2"), index_dir=tmp_path)
    assert other_model.reembedded_files == ["sandbox_design.md"]

    changed = tmp_path / "changed.md"
    changed.write_text(DESIGN_DOC.read_text(encoding="utf-8") + "\n## New\nmore.\n", "utf-8")
    both = build_index([DESIGN_DOC, changed], FakeProvider(), index_dir=tmp_path)
    assert both.reembedded_files == ["changed.md"]
    assert len(both) == len(index) * 2 + 1 and both.matrix.shape[0] == len(both)


def test_cache_key_covers_chunk_settings() -> None:
    from qalab.rag.index import cache_key

    base = cache_key("text", "m", 800, 100)
    assert cache_key("text", "m", 400, 100) != base and cache_key("text", "m", 800, 50) != base
    assert cache_key("text", "m", 800, 100) == base
