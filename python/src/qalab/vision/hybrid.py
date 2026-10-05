"""Spec 03 hybrid policy: heuristics first, the VLM only where it can add something.

Heuristics cover black_screen, missing_texture and placeholder_ui for free. The VLM is called only
when no heuristic fired AND either the frame is close to a UI action or detector event (where UI
bugs show up) or it is every Nth remaining frame (a sparse sample of the rest).
"""

from __future__ import annotations

import bisect
from collections.abc import Sequence
from dataclasses import dataclass


@dataclass
class HybridPolicy:
    every_n: int = 5
    window_s: float = 2.0
    _remaining: int = 0

    def __post_init__(self) -> None:
        if self.every_n < 1:
            raise ValueError("every_n must be >= 1")

    def should_call_vlm(self, heuristics_fired: bool, event_gap_s: float | None) -> bool:
        """Decide for the next frame (call in frame order). ``event_gap_s``: seconds to the nearest
        UI action or detector event, or None when the run has none."""
        if heuristics_fired:
            return False
        if event_gap_s is not None and event_gap_s <= self.window_s:
            return True
        self._remaining += 1
        return (self._remaining - 1) % self.every_n == 0


def event_gaps(frame_times: Sequence[float], event_times: Sequence[float]) -> list[float | None]:
    """For each frame time, the seconds to the nearest event time (None if there are no events)."""
    events = sorted(event_times)
    if not events:
        return [None] * len(frame_times)
    gaps: list[float | None] = []
    for t in frame_times:
        i = bisect.bisect_left(events, t)
        nearest = min(abs(t - events[j]) for j in (i - 1, i) if 0 <= j < len(events))
        gaps.append(round(nearest, 3))
    return gaps
