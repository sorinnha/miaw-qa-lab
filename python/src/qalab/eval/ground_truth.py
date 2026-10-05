"""Seeded-bug ground truth for evaluation (spec 00 ``labels.json`` → ``match`` rules).

This module and the rest of ``qalab.eval`` are the only code allowed to open ``labels.json``;
triage and vision must never import it (a test enforces the file side of that rule).
"""

from __future__ import annotations

import json
import math
import re
from collections.abc import Iterable, Mapping
from dataclasses import dataclass, field
from pathlib import Path

from qalab.models.event import Event
from qalab.models.labels import Labels, MatchRule, SeededBug

LABELS_FILE = "labels.json"
# (run_id, seq, visual detector or ""). Vision events share their screenshot's seq, so one frame
# with two labels gives two events with the same seq: the detector name keeps them apart.
EventKey = tuple[str, int, str]

# "## Doors" → "Doors"
_H2 = re.compile(r"^## (.+?)\s*$")
# "Components: `SeededDoor`, `Interactor`." → " `SeededDoor`, `Interactor`."
_COMPONENT_LINE = re.compile(r"Components?:([^\n]*)")
# " `SeededDoor`, `Interactor`." → ["SeededDoor", "Interactor"]
_BACKTICKED = re.compile(r"`([^`]+)`")


def load_labels(run_dir: Path) -> Labels | None:
    """The run's ``labels.json``, or None when the run was not a benchmark run."""
    path = Path(run_dir) / LABELS_FILE
    if not path.is_file():
        return None
    with path.open(encoding="utf-8") as f:
        return Labels.model_validate(json.load(f))


def rule_matches(rule: MatchRule, event: Event) -> bool:
    """Spec 00: every key that is set must match. ``visual_label`` never matches a raw event
    (it is matched against vision findings, M6), and a rule with no usable key matches nothing."""
    if rule.visual_label is not None:
        return False
    checked = False
    if rule.stack_contains is not None:
        if not event.stack or rule.stack_contains not in event.stack:
            return False
        checked = True
    if rule.message_regex is not None:
        if event.message is None or re.search(rule.message_regex, event.message) is None:
            return False
        checked = True
    if rule.detector is not None:
        if event.kind != "detector" or (event.data or {}).get("detector") != rule.detector:
            return False
        checked = True
    if rule.near is not None:
        if event.pos is None:
            return False
        radius = rule.radius if rule.radius is not None else 0.0
        dx, dz = event.pos[0] - rule.near[0], event.pos[2] - rule.near[2]
        if math.hypot(dx, dz) > radius:
            return False
        checked = True
    return checked


@dataclass
class GroundTruth:
    """Which seeded bug each event is, across all benchmark runs."""

    bugs: dict[str, SeededBug] = field(default_factory=dict)  # bug_id → catalog entry
    event_bug: dict[EventKey, str] = field(default_factory=dict)  # events matching exactly one bug
    ambiguous: list[EventKey] = field(default_factory=list)  # events matching two or more bugs
    runs_with_labels: list[str] = field(default_factory=list)
    runs_without_labels: list[str] = field(default_factory=list)

    def bug_of(self, event: Event) -> str | None:
        return self.event_bug.get(event_key(event))


def event_key(event: Event) -> EventKey:
    """The ground-truth key of an event: ``visual:<label>`` events are told apart by label."""
    label = _visual_label(event)
    return (event.run_id, event.seq, f"visual:{label}" if label is not None else "")


def build_ground_truth(
    events_by_run: Mapping[str, Iterable[Event]], labels_by_run: Mapping[str, Labels | None]
) -> GroundTruth:
    """Match every event against the seeded bugs triggered in its own run.

    Only seeds listed in that run's labels count: a seed that never fired in a run is not ground
    truth for it. An event matching two seeds is ambiguous and left out (the sandbox's catalog
    tests keep rules disjoint, so this should not happen). A ``visual:<label>`` event (from vision
    findings) belongs to a visual seed only when ``labels.json`` says its screenshot shows that seed
    with that label, so a false positive on a clean frame never counts as finding the bug.
    """
    truth = GroundTruth()
    for run_id, events in events_by_run.items():
        labels = labels_by_run.get(run_id)
        if labels is None:
            truth.runs_without_labels.append(run_id)
            continue
        truth.runs_with_labels.append(run_id)
        rules = [b for b in labels.seeded_bugs if b.match.visual_label is None]
        visual = {
            b.bug_id: b.match.visual_label for b in labels.seeded_bugs if b.match.visual_label
        }
        shots = {shot.path: set(shot.bug_ids or []) for shot in labels.screenshots}
        for bug in labels.seeded_bugs:
            truth.bugs.setdefault(bug.bug_id, bug)
        for event in events:
            if _visual_label(event) is not None:
                shown = shots.get((event.data or {}).get("screenshot"), set())
                hits = [b for b in sorted(shown) if visual.get(b) == _visual_label(event)]
            else:
                hits = [b.bug_id for b in rules if rule_matches(b.match, event)]
            if len(hits) == 1:
                truth.event_bug[event_key(event)] = hits[0]
            elif len(hits) > 1:
                truth.ambiguous.append(event_key(event))
    truth.runs_with_labels.sort()
    truth.runs_without_labels.sort()
    return truth


def _visual_label(event: Event) -> str | None:
    """``missing_texture`` for a ``visual:missing_texture`` detector event, else None."""
    if event.kind != "detector":
        return None
    detector = str((event.data or {}).get("detector", ""))
    return detector.split(":", 1)[1] if detector.startswith("visual:") else None


def feature_components(design_doc: str) -> dict[str, list[str]]:
    """``## Feature`` → the backticked names on its ``Component:``/``Components:`` line.

    Used to score whether a report's component names the right part of the game.
    """
    components: dict[str, list[str]] = {}
    current: str | None = None
    for line in design_doc.splitlines():
        heading = _H2.match(line)
        if heading:
            current = heading.group(1)
            components[current] = []
            continue
        if current is None:
            continue
        for sentence in _COMPONENT_LINE.findall(line):
            components[current].extend(_BACKTICKED.findall(sentence))
    return components
