"""``qalab eval vision``: per-label scores, tuning on val, the hybrid trade-off (spec 03, D-029).

Scoring and the hybrid simulation are checked on hand-made examples.
"""

import json
from pathlib import Path

import numpy as np
import pytest
from PIL import Image
from typer.testing import CliRunner

from qalab.cli import app
from qalab.eval.vision_dataset import Frame, build_dataset
from qalab.eval.vision_eval import (
    HYBRID_N_GRID,
    HYBRID_WINDOW_GRID,
    VisionEvalOptions,
    VisionEvalResult,
    evaluate_vision,
    score_predictions,
    simulate_hybrid,
    to_markdown,
    tune_hybrid,
    tune_thresholds,
    write_result,
)
from qalab.llm.fake import FakeProvider
from qalab.vision.heuristics import Stats, Thresholds

from .bench import make_benchmark

runner = CliRunner()


def test_scores_on_a_hand_checked_example() -> None:
    # 3 frames, 2 labels in play:
    #   frame 0: truth {missing_texture}  predicted {missing_texture, black_screen}
    #   frame 1: truth {black_screen}     predicted {}
    #   frame 2: truth {}                 predicted {missing_texture}
    truth = [{"missing_texture"}, {"black_screen"}, set()]
    predicted = [{"missing_texture", "black_screen"}, set(), {"missing_texture"}]
    score = score_predictions("toy", "test", truth, predicted)

    mt = score.per_label["missing_texture"]  # TP frame 0, FP frame 2
    assert (mt.tp, mt.fp, mt.fn, mt.support) == (1, 1, 0, 1)
    assert (mt.precision, mt.recall, mt.f1) == (0.5, 1.0, pytest.approx(0.6667))
    bs = score.per_label["black_screen"]  # FP frame 0, FN frame 1
    assert (bs.tp, bs.fp, bs.fn) == (0, 1, 1)
    assert (bs.precision, bs.recall, bs.f1) == (0.0, 0.0, 0.0)
    ui = score.per_label["ui_overflow"]  # never true, never predicted: nothing to measure
    assert (ui.precision, ui.recall, ui.f1, ui.support) == (None, None, None, 0)

    assert score.macro_f1 == pytest.approx((0.6667 + 0.0) / 2, abs=1e-4), "labels with support only"
    assert score.macro_recall == 0.5
    assert score.fp_per_100 == pytest.approx(100 * 2 / 3, abs=1e-3), "2 false positives, 3 frames"
    assert mt.fp_per_100 == bs.fp_per_100 == pytest.approx(100 / 3, abs=1e-3), (
        "1 each (H1 per label)"
    )


def test_scores_reject_mismatched_lengths() -> None:
    with pytest.raises(ValueError):
        score_predictions("x", "test", [set()], [])


def test_black_threshold_is_tuned_on_val_and_magenta_keeps_its_default() -> None:
    stats = [Stats(r, 0.0, ()) for r in (0.98, 0.94, 0.20, 0.92)]
    truth = [{"black_screen"}, {"black_screen"}, set(), set()]
    thresholds, notes = tune_thresholds(stats, truth)
    # 0.90 also catches the 0.92 frame (F1 0.8); 0.95 misses the 0.94 one (F1 0.67); 0.93 is exact.
    assert thresholds.black_ratio == 0.93
    assert thresholds.magenta_ratio == 0.003
    assert "missing_texture" in notes and "black_screen" not in notes


def _frame(run: str, gap: float | None) -> Frame:
    return Frame(f"images/{run}/x.png", run, 0.0, "L", [], "val", gap, "")


def test_hybrid_calls_near_events_every_nth_frame_and_restarts_per_run() -> None:
    frames = [
        _frame("a", None),  # heuristics fired: no call
        _frame("a", 0.5),  # within the 1 s window: call
        _frame("a", None),  # 1st remaining frame: call
        _frame("a", 5.0),  # 2nd remaining frame: skipped (every 2nd)
        _frame("a", None),  # 3rd remaining frame: call
        _frame("b", None),  # new run, new policy: its 1st remaining frame is called
    ]
    heuristic = [{"black_screen"}, set(), set(), set(), set(), set()]
    vlm = [set(), {"ui_overflow"}, set(), {"ui_overflow"}, set(), set()]
    predicted, called = simulate_hybrid(frames, heuristic, vlm, every_n=2, window_s=1.0)
    assert called == [False, True, True, False, True, True]
    assert predicted == [{"black_screen"}, {"ui_overflow"}, set(), set(), set(), set()]


def test_hybrid_tuning_picks_the_cheapest_setting_that_keeps_recall() -> None:
    frames = [_frame("a", None) for _ in range(4)]
    clean = [set(), set(), set(), set()]
    # The bug is on frame 2. Every N calls frames 0, N, 2N...: only N = 1 (4 calls) and N = 2
    # (2 calls) reach it, so N = 2 is the cheapest with ≥ 90% of the VLM's recall.
    truth = [set(), set(), {"ui_overflow"}, set()]
    n, window, points = tune_hybrid(frames, truth, clean, truth)
    assert (n, window) == (2, 0.0)
    assert len(points) == len(HYBRID_N_GRID) * len(HYBRID_WINDOW_GRID)
    # On frame 0 even N = 5 finds it with 1 call; ties go to the smaller N.
    first = [{"ui_overflow"}, set(), set(), set()]
    assert tune_hybrid(frames, first, clean, first)[:2] == (5, 0.0)


def test_markdown_and_files(tmp_path: Path) -> None:
    dataset = tmp_path / "ds"
    (dataset / "images" / "r1").mkdir(parents=True)
    Image.fromarray(np.zeros((9, 16, 3), dtype=np.uint8)).save(dataset / "images/r1/000005.png")
    score = score_predictions("heuristic", "test", [{"black_screen"}, set()], [set(), set()])
    result = VisionEvalResult(
        dataset=str(dataset),
        created_at="2026-10-05T00:00:00Z",
        qalab_version="0.1.0",
        frames={"test": 2},
        scores=[score],
        thresholds={"black_ratio": 0.9, "magenta_ratio": 0.003},
        skipped={"vlm": "needs a vision provider"},
        errors=[
            {
                "kind": "miss",
                "method": "heuristic",
                "path": "images/r1/000005.png",
                "label": "black_screen",
                "predicted": "nothing",
            }
        ],
    )
    text = to_markdown(result)
    assert "| heuristic | test | n/a/n/a | n/a/0.00 |" in text, "precision n/a: nothing predicted"
    assert "| vlm | skipped: needs a vision provider |" in text
    assert "- miss `black_screen` on `images/r1/000005.png` (predicted nothing)" in text
    written = write_result(result, dataset, tmp_path / "eval", "h1 test")
    assert [p.name for p in written] == [
        "vision_h1_test.json",
        "vision_h1_test.md",
        "vision_h1_test_prf.png",
    ]
    assert (
        tmp_path / "eval" / "vision_h1_test_errors" / "miss_black_screen_r1_000005.png"
    ).is_file()


def test_evaluation_needs_a_test_split(tmp_path: Path) -> None:
    dataset = tmp_path / "ds"
    build_dataset([make_benchmark(tmp_path / "bench", runs=2)], dataset)  # 2 runs: all train
    with pytest.raises(ValueError, match="no test frames"):
        evaluate_vision(dataset, VisionEvalOptions())


def test_cli_rejects_unknown_methods(tmp_path: Path) -> None:
    result = runner.invoke(app, ["eval", "vision", str(tmp_path), "--methods", "magic"])
    assert result.exit_code == 2 and "unknown method" in result.output


def _dataset(tmp_path: Path) -> Path:
    dataset = tmp_path / "ds"
    build_dataset([make_benchmark(tmp_path / "bench", runs=3)], dataset)  # 1 train, 1 val, 1 test
    return dataset


def _seen(dataset: Path) -> dict[str, list[dict[str, object]]]:
    """What the fake VLM "sees": the two seeded glitches, and a wrong ui_overflow on shot 2."""
    seen: dict[str, list[dict[str, object]]] = {}
    for png in dataset.glob("images/*/*.png"):
        path = png.relative_to(dataset).as_posix()
        seen[path] = {
            "000002.png": [{"label": "ui_overflow", "score": 0.8}],
            "000004.png": [{"label": "missing_texture", "score": 0.9}],
            "000005.png": [{"label": "black_screen", "score": 0.95}],
        }.get(png.name, [])
    return seen


def test_full_evaluation_with_the_fake_vlm(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path)
    provider = FakeProvider(vision_labels=_seen(dataset))
    options = VisionEvalOptions(methods=("heuristic", "vlm", "ml", "hybrid"), provider=provider)
    result = evaluate_vision(dataset, options)
    scores = {s.method: s for s in result.scores}
    assert set(scores) == {"heuristic", "vlm", "ml", "hybrid"}
    assert all(s.split == "test" and s.frames == 5 for s in result.scores)

    vlm = scores["vlm"]
    assert vlm.vlm_calls == 5
    assert vlm.per_label["missing_texture"].recall == vlm.per_label["black_screen"].recall == 1.0
    assert vlm.per_label["ui_overflow"].fp == 1 and vlm.fp_per_100 == 20.0
    heuristic = scores["heuristic"]
    assert heuristic.vlm_calls == 0
    assert heuristic.per_label["missing_texture"].recall == 1.0
    assert heuristic.per_label["black_screen"].recall == 1.0
    hybrid = scores["hybrid"]
    assert hybrid.vlm_calls < vlm.vlm_calls, "frames the heuristics already flagged skip the VLM"
    assert hybrid.macro_recall == 1.0
    assert result.hybrid_test_curve and result.hybrid_val
    assert set(result.ml_overfit) == {"train_macro_f1", "test_macro_f1"}
    assert len(provider.calls) == 10, "one VLM answer per val and test frame, reused by the hybrid"
    assert hybrid.latency_p95_ms is not None, "heuristics plus the VLM where it was called"
    assert all(e["method"] == "hybrid" for e in result.errors), "examples come from the hybrid"


def test_vlm_only_asks_about_test_frames_and_leads_the_error_examples(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path)
    provider = FakeProvider(vision_labels=_seen(dataset))
    result = evaluate_vision(dataset, VisionEvalOptions(methods=("vlm",), provider=provider))
    assert [s.method for s in result.scores] == ["vlm"]
    assert len(provider.calls) == 5, "no hybrid to tune: val frames are not sent"
    assert result.thresholds == {}, "no heuristics requested, none computed"
    assert [(e["kind"], e["label"], e["method"]) for e in result.errors] == [
        ("false_positive", "ui_overflow", "vlm")
    ]


def test_unusable_vlm_answers_are_counted_and_flagged(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path)
    broken = FakeProvider(invalid_json_times=1000)
    result = evaluate_vision(dataset, VisionEvalOptions(methods=("vlm",), provider=broken))
    assert result.vlm_errors == {"test": 5}
    assert result.scores[0].macro_recall == 0.0
    assert "Warning: 5 test frame(s) got no usable VLM answer" in to_markdown(result)


def test_the_shipped_thresholds_get_their_own_row_when_they_differ(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path)
    options = VisionEvalOptions(
        methods=("heuristic",), configured=Thresholds(black_ratio=0.5, magenta_ratio=0.05)
    )
    rows = [s.method for s in evaluate_vision(dataset, options).scores]
    assert rows == ["heuristic", "heuristic (qalab.toml)"]


def test_cli_without_a_vision_provider_scores_heuristics_only(tmp_path: Path) -> None:
    dataset = _dataset(tmp_path)
    out = tmp_path / "eval"
    args = ["eval", "vision", str(dataset), "--provider", "none", "--out", str(out), "--label", "h"]
    result = runner.invoke(app, args)
    if isinstance(result.exception, NotImplementedError):
        raise result.exception
    assert result.exit_code == 0, result.output
    data = json.loads((out / "vision_h.json").read_text(encoding="utf-8"))
    assert [s["method"] for s in data["scores"]] == ["heuristic"]
    assert set(data["skipped"]) == {"vlm", "hybrid"}
