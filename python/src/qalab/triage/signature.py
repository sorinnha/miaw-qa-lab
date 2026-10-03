"""Spec 02 §4: a short stable id per bug, from what stays the same between builds and runs.

Line numbers are left out on purpose: they move with every edit and would split one bug into
many clusters. Frames (class + method) and the normalized message carry the identity.
"""

from __future__ import annotations

import hashlib
import math
import re

from qalab.models.event import Event
from qalab.triage.normalize import normalize_message
from qalab.triage.stack import app_frames_of

# "KeyNotFoundException: The given key" → "KeyNotFoundException"; "Inventory slot 7" → no match
EXCEPTION_TYPE = re.compile(r"^\s*(\w+(?:Exception|Error))\b")

DEFAULT_CELL_SIZE_M = 4.0
CANDIDATE_LEVELS = frozenset({"warning", "error", "exception", "assert"})


def short_sha1(text: str) -> str:
    return hashlib.sha1(text.encode("utf-8")).hexdigest()[:12]


def exception_type_of(message: str | None) -> str:
    match = EXCEPTION_TYPE.match(message or "")
    return match.group(1) if match else ""


def log_signature(event: Event) -> tuple[str, str, str, list[str]]:
    """``(signature, exception_type, normalized_message, app_frames)`` for a log event."""
    frames = [f.qualified for f in app_frames_of(event.stack)]
    exception_type = exception_type_of(event.message)
    normalized = normalize_message(event.message or "")
    f1, f2, f3 = (frames + ["", "", ""])[:3]
    key = f"log|{event.level}|{exception_type}|{normalized}|{f1}|{f2}|{f3}"
    return short_sha1(key), exception_type, normalized, frames


def cell_of(pos: tuple[float, float, float] | None, cell_size_m: float) -> tuple[int, int]:
    """World position → ground-plane grid cell ``(floor(x / size), floor(z / size))``."""
    if pos is None:
        return (0, 0)
    return (math.floor(pos[0] / cell_size_m), math.floor(pos[2] / cell_size_m))


def detector_signature(
    event: Event, cell_size_m: float = DEFAULT_CELL_SIZE_M
) -> tuple[str, str, tuple[int, int]]:
    """``(signature, detector, cell)`` for a detector event (also ``visual:<label>`` ones)."""
    detector = event.detector().detector
    cell = cell_of(event.pos, cell_size_m)
    key = f"det|{detector}|{event.scene or ''}|{cell}"
    return short_sha1(key), detector, cell


def is_candidate(event: Event, min_level: str = "warning") -> bool:
    """Warnings and up, plus every detector. ``info`` logs are context only."""
    if event.kind == "detector":
        return True
    if event.kind != "log":
        return False
    order = ["info", "warning", "error", "exception", "assert"]
    return event.level in CANDIDATE_LEVELS and order.index(event.level) >= order.index(min_level)
