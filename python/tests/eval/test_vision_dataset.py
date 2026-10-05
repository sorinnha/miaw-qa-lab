"""``qalab vision dataset``: labelled screenshots → a dataset split by run (spec 03).

No learning task runs here: building the dataset copies files and reads labels, it doesn't look at
pixels.
"""

import json
from pathlib import Path

import pytest
from typer.testing import CliRunner

from qalab.cli import app
from qalab.eval.vision_dataset import (
    Frame,
    build_dataset,
    dataset_stats,
    load_dataset,
    split_runs,
)

from .bench import make_benchmark

runner = CliRunner()


def test_split_is_by_run_seeded_and_70_15_15() -> None:
    ids = [f"run{i:02d}" for i in range(20)]
    split = split_runs(ids, seed=7)
    assert sorted(split) == ids, "every run gets exactly one split"
    counts = {s: list(split.values()).count(s) for s in ("train", "val", "test")}
    assert counts == {"train": 14, "val": 3, "test": 3}
    assert split_runs(list(reversed(ids)), seed=7) == split, "input order doesn't matter"
    assert split_runs(ids, seed=8) != split, "the seed decides which runs go where"


def test_small_benchmarks_still_get_a_val_and_a_test_run() -> None:
    assert sorted(split_runs(["a", "b", "c"]).values()) == ["test", "train", "val"]
    # Fewer than 3 runs can't fill three splits: everything trains (evaluation then refuses).
    assert set(split_runs(["a", "b"]).values()) == {"train"}


def test_frame_rows_round_trip_including_missing_values() -> None:
    frame = Frame("images/r/000001.png", "r", None, "Menu", [], "val", None, "camera_render")
    assert frame.row()["t"] == "" and frame.row()["labels"] == ""
    assert Frame.from_row(frame.row()) == frame
    labelled = Frame(
        "images/r/2.png", "r", 30.0, "L1", ["black_screen", "ui_overflow"], "test", 0.5, ""
    )
    assert labelled.row()["labels"] == "black_screen|ui_overflow"
    assert Frame.from_row(labelled.row()) == labelled


def test_build_copies_labelled_shots_and_skips_unlabelled_runs(tmp_path: Path) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=3, unlabelled=1)
    out = tmp_path / "ds"
    frames = build_dataset([bench], out, seed=7)

    assert len(frames) == 15, "3 labelled runs x 5 labelled shots; the unlabelled run adds none"
    assert all((out / f.path).is_file() for f in frames)
    assert load_dataset(out) == frames, "index.csv holds exactly what was built"
    for run_id in {f.run_id for f in frames}:
        assert len({f.split for f in frames if f.run_id == run_id}) == 1, "never split a run"
    assert sorted({f.split for f in frames}) == ["test", "train", "val"]

    by_name = {Path(f.path).name: f for f in frames if f.split == "test"}
    assert by_name["000004.png"].labels == ["missing_texture"]
    assert by_name["000005.png"].labels == ["black_screen"]
    assert by_name["000001.png"].labels == [], "a clean frame is a negative example"
    # Shot 3 is taken 0.01 s after the fall detector (t 26.9 → 26.91): the hybrid's "near an event".
    assert by_name["000003.png"].event_gap_s == pytest.approx(0.01)
    assert by_name["000003.png"].method == "screen_capture"

    stats = json.loads((out / "stats.json").read_text(encoding="utf-8"))
    assert stats["splits"]["test"]["labels"]["black_screen"] == 1
    assert stats["splits"]["test"]["clean"] == 3
    assert any("'black_screen'" in w for w in stats["warnings"]), "1 test frame is too few"


def test_a_shot_without_its_file_is_skipped(tmp_path: Path) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=1)
    (next(bench.iterdir()) / "shots" / "000002.png").unlink()
    frames = build_dataset([bench], tmp_path / "ds")
    assert [Path(f.path).name for f in frames] == [
        "000001.png",
        "000003.png",
        "000004.png",
        "000005.png",
    ]


def test_build_needs_labelled_runs(tmp_path: Path) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=0, unlabelled=2)
    with pytest.raises(ValueError, match="labels.json"):
        build_dataset([bench], tmp_path / "ds")


def test_stats_warn_only_for_thin_test_labels() -> None:
    frames = [
        Frame(f"images/r/{i}.png", "r", float(i), "L", ["black_screen"], "test", None, "")
        for i in range(12)
    ]
    warnings = dataset_stats(frames)["warnings"]
    assert not any("'black_screen'" in w for w in warnings)  # type: ignore[union-attr]
    assert any("'missing_texture'" in w for w in warnings)  # type: ignore[union-attr]


def test_cli_dataset(tmp_path: Path) -> None:
    bench = make_benchmark(tmp_path / "bench", runs=3)
    result = runner.invoke(app, ["vision", "dataset", str(bench), "--out", str(tmp_path / "ds")])
    assert result.exit_code == 0, result.output
    assert "test: 5 frames from 1 runs" in result.output
    assert (tmp_path / "ds" / "index.csv").is_file()
    bad = runner.invoke(
        app, ["vision", "dataset", str(tmp_path / "nothing"), "--out", str(tmp_path / "x")]
    )
    assert bad.exit_code == 2
