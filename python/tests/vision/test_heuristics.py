"""Spec 03 heuristics on synthetic frames and on the sample run's screenshots.

Anything that runs ``magenta_ratio`` (Sora's, YOU WRITE) carries the marker, including ``analyze``.
"""

import numpy as np
import pytest

from qalab.vision import heuristics
from qalab.vision.heuristics import Thresholds, black_ratio, magenta_ratio, to_score, white_boxes
from qalab.vision.images import load_rgb

from ..eval.bench import SAMPLE_RUN

H, W = 360, 640


def frame(color=(90, 140, 90)) -> np.ndarray:
    img = np.zeros((H, W, 3), dtype=np.uint8)
    img[:] = color
    return img


def gradient() -> np.ndarray:
    x = np.linspace(0, 200, W, dtype=np.uint8)
    img = np.zeros((H, W, 3), dtype=np.uint8)
    img[..., 0] = x
    img[..., 1] = 120
    img[..., 2] = x[::-1]
    return img


def with_patch(img: np.ndarray, share: float, color) -> np.ndarray:
    side = int(round((share * H * W) ** 0.5))
    img = img.copy()
    img[10 : 10 + side, 10 : 10 + side] = color
    return img


# ---- black screen (no learning task) ---------------------------------------------------------


def test_black_ratio_counts_dark_pixels_and_tolerates_a_hud() -> None:
    assert black_ratio(frame((0, 0, 0))) == 1.0
    hud = with_patch(frame((0, 0, 0)), 0.05, (255, 255, 255))
    assert 0.90 <= black_ratio(hud) < 1.0
    assert black_ratio(frame((20, 20, 20))) == 0.0, "luma 20 is dark grey, not black"
    assert black_ratio(np.zeros((0, 0, 3), dtype=np.uint8)) == 0.0


def test_white_box_found_but_not_white_noise_or_a_thin_line() -> None:
    box = with_patch(frame(), 0.01, (255, 255, 255))
    found = white_boxes(box)
    assert len(found) == 1 and found[0].fill == pytest.approx(1.0)
    rng = np.random.default_rng(3)
    noise = frame().copy()
    noise[rng.random((H, W)) < 0.05] = 255
    assert white_boxes(noise) == [], "scattered white pixels are not a box"
    line = frame().copy()
    line[100:102, 0:600] = 255  # a 2 px tall bar: aspect far outside 0.2–5
    assert white_boxes(line) == []


def test_score_is_monotonic_with_the_threshold_as_the_cut() -> None:
    assert to_score(0.95, 0.9) == 1.0
    assert to_score(0.9, 0.9) == 1.0
    assert to_score(0.45, 0.9) == pytest.approx(0.245)
    assert to_score(0.0, 0.9) == 0.0
    assert to_score(0.1, 0.9) < to_score(0.2, 0.9) < 0.5


# ---- magenta and the full analysis (YOU WRITE: magenta_ratio) --------------------------------


@pytest.mark.youwrite
def test_magenta_ratio_matches_the_spec_rule() -> None:
    assert magenta_ratio(frame((255, 0, 255))) == 1.0
    assert magenta_ratio(frame((90, 140, 90))) == 0.0
    patch = with_patch(frame(), 0.005, (230, 40, 220))
    assert magenta_ratio(patch) == pytest.approx(0.005, rel=0.1)
    assert type(magenta_ratio(patch)) is float
    assert magenta_ratio(np.zeros((0, 0, 3), dtype=np.uint8)) == 0.0


@pytest.mark.youwrite
def test_magenta_edges_and_the_uint8_trap() -> None:
    assert magenta_ratio(frame((181, 79, 181))) == 1.0, "just inside every bound"
    assert magenta_ratio(frame((180, 0, 200))) == 0.0, "R must be > 180 (180 is not)"
    assert magenta_ratio(frame((200, 0, 180))) == 0.0, "B must be > 180 (180 is not)"
    assert magenta_ratio(frame((200, 0, 150))) == 0.0, "B too low, though |R − B| = 50 is close"
    assert magenta_ratio(frame((255, 80, 255))) == 0.0, "G must be < 80"
    assert magenta_ratio(frame((250, 0, 190))) == 0.0, "|R − B| = 60 is not < 60"
    assert magenta_ratio(frame((249, 0, 190))) == 1.0, "|R − B| = 59 is"
    # |200 − 250| = 50 counts. With uint8 arithmetic 200 − 250 wraps around to 206 and it wouldn't.
    assert magenta_ratio(frame((200, 0, 250))) == 1.0


@pytest.mark.youwrite
def test_analyze_synthetic_frames() -> None:
    labels = lambda img: sorted(f.label for f in heuristics.analyze(img))  # noqa: E731
    assert labels(gradient()) == [], "a clean frame"
    assert labels(with_patch(frame((0, 0, 0)), 0.05, (255, 255, 255))) == [
        "black_screen",
        "placeholder_ui",
    ]
    assert labels(with_patch(gradient(), 0.005, (255, 0, 255))) == ["missing_texture"]
    assert labels(with_patch(gradient(), 0.001, (255, 0, 255))) == [], (
        "0.1% is under the 0.3% threshold"
    )
    strict = Thresholds(magenta_ratio=0.0005)
    assert [
        f.label for f in heuristics.analyze(with_patch(gradient(), 0.001, (255, 0, 255)), strict)
    ] == ["missing_texture"]


@pytest.mark.youwrite
@pytest.mark.parametrize(
    ("shot", "expected"),
    [
        ("000001.png", []),
        ("000002.png", []),
        ("000003.png", []),
        ("000004.png", ["missing_texture"]),
        ("000005.png", ["black_screen"]),
    ],
)
def test_sample_run_shots_match_expected_md(shot: str, expected: list[str]) -> None:
    found = heuristics.analyze(load_rgb(SAMPLE_RUN / "shots" / shot))
    assert sorted(f.label for f in found) == expected
