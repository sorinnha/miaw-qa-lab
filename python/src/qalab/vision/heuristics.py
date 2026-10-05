"""Spec 03 heuristics: cheap pixel statistics for three of the four labels.

Each works on RGB downscaled to a long side of 640 and returns a statistic; ``analyze`` turns the
statistics into labels with scores. Thresholds start at the spec's values and are tuned on the
validation split only (``qalab eval vision``). ``ui_overflow`` is not attempted: text spilling out
of a box has no simple pixel signature (a known gap, H2).
"""

from __future__ import annotations

from dataclasses import asdict, dataclass

import numpy as np
from scipy import ndimage

from qalab.models.visual import FoundLabel
from qalab.vision.images import downscale, luminance

WORK_LONG_SIDE = 640


@dataclass(frozen=True)
class Thresholds:
    black_ratio: float = 0.90  # share of near-black pixels (luma < 16); the HUD may cover the rest
    magenta_ratio: float = 0.003  # share of magenta pixels
    white_min_area: float = 0.002  # a near-white component covering ≥ 0.2% of the frame ...
    white_min_fill: float = 0.9  # ... that fills ≥ 90% of its bounding box ...
    white_aspect: tuple[float, float] = (0.2, 5.0)  # ... with width / height in this range

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


DEFAULT_THRESHOLDS = Thresholds()  # frozen, so one shared default is safe


def black_ratio(rgb: np.ndarray) -> float:
    """Share of pixels whose luminance is below 16 (``black_screen``)."""
    if rgb.size == 0:
        return 0.0
    return float(np.mean(luminance(rgb) < 16.0))


def magenta_ratio(rgb: np.ndarray) -> float:
    """Share of pixels that look like Unity's missing-material magenta (``missing_texture``).

    YOU WRITE (Sora, M6). A pixel counts when, with ``r, g, b`` as ints:
    ``r > 180 and g < 80 and b > 180 and abs(r - b) < 60``. Return the share of such pixels in the
    whole image as a plain ``float`` between 0 and 1; an empty image (no pixels) returns 0.0.

    Do it with numpy masks, not a Python loop over pixels (a 640×360 frame has 230 400 of them):
    ``r = rgb[..., 0].astype(np.int16)`` gives the red channel as a 2-D array; comparisons like
    ``r > 180`` give boolean arrays you combine with ``&``; ``np.mean`` of a boolean array is the
    share of True. Why ``int16``: ``uint8`` arithmetic wraps around, so ``r - b`` with r = 200 and
    b = 250 would be 206, not −50, and a real magenta pixel would be missed.

    C# comparison: the same as ``pixels.Count(p => p.R > 180 && ...) / (double)pixels.Length``,
    vectorized.
    """
    raise NotImplementedError("YOU WRITE")


@dataclass(frozen=True)
class WhiteBox:
    """A near-white rectangle candidate, in normalized coordinates (0–1)."""

    x: float
    y: float
    w: float
    h: float
    area: float  # share of the frame
    fill: float  # pixels / bounding-box area


def white_boxes(rgb: np.ndarray, thresholds: Thresholds = DEFAULT_THRESHOLDS) -> list[WhiteBox]:
    """Connected components of near-white pixels (every channel ≥ 245) that look like a solid box
    (``placeholder_ui``): big enough, nearly filling their bounding box, not a thin line."""
    height, width = rgb.shape[:2]
    if height == 0 or width == 0:
        return []
    mask = np.all(rgb >= 245, axis=-1)
    labels, count = ndimage.label(mask)
    if count == 0:
        return []
    boxes: list[WhiteBox] = []
    sizes = ndimage.sum_labels(mask, labels, index=np.arange(1, count + 1))
    for index, region in enumerate(ndimage.find_objects(labels), start=1):
        if region is None:
            continue
        rows, cols = region
        box_h = rows.stop - rows.start
        box_w = cols.stop - cols.start
        pixels = float(sizes[index - 1])
        area = pixels / (height * width)
        fill = pixels / (box_h * box_w)
        aspect = box_w / box_h
        low, high = thresholds.white_aspect
        if (
            area >= thresholds.white_min_area
            and fill >= thresholds.white_min_fill
            and low <= aspect <= high
        ):
            boxes.append(
                WhiteBox(
                    cols.start / width,
                    rows.start / height,
                    box_w / width,
                    box_h / height,
                    area,
                    fill,
                )
            )
    return boxes


def to_score(stat: float, threshold: float) -> float:
    """Monotonic score: 1.0 at or above the threshold, else under 0.5, in proportion to the stat.
    Triage turns a label into a bug at score ≥ 0.5, so the threshold decides."""
    if threshold <= 0:
        return 1.0
    if stat >= threshold:
        return 1.0
    return round(0.49 * max(stat, 0.0) / threshold, 4)


@dataclass(frozen=True)
class Stats:
    """The three statistics of one frame (computed once, scored under any thresholds)."""

    black_ratio: float
    magenta_ratio: float
    white_boxes: tuple[WhiteBox, ...]


def frame_stats(rgb: np.ndarray, thresholds: Thresholds = DEFAULT_THRESHOLDS) -> Stats:
    small = downscale(rgb, WORK_LONG_SIDE)
    return Stats(black_ratio(small), magenta_ratio(small), tuple(white_boxes(small, thresholds)))


def labels_from_stats(
    stats: Stats, thresholds: Thresholds = DEFAULT_THRESHOLDS
) -> list[FoundLabel]:
    """Labels whose score reaches 0.5, best box as the placeholder region."""
    found: list[FoundLabel] = []
    black = to_score(stats.black_ratio, thresholds.black_ratio)
    if black >= 0.5:
        found.append(FoundLabel(label="black_screen", score=black))
    magenta = to_score(stats.magenta_ratio, thresholds.magenta_ratio)
    if magenta >= 0.5:
        found.append(FoundLabel(label="missing_texture", score=magenta))
    if stats.white_boxes:
        best = max(stats.white_boxes, key=lambda b: b.area)
        found.append(
            FoundLabel(
                label="placeholder_ui",
                score=1.0,
                region=(round(best.x, 4), round(best.y, 4), round(best.w, 4), round(best.h, 4)),
            )
        )
    return found


def analyze(rgb: np.ndarray, thresholds: Thresholds = DEFAULT_THRESHOLDS) -> list[FoundLabel]:
    """Heuristic labels for one screenshot."""
    return labels_from_stats(frame_stats(rgb, thresholds), thresholds)
