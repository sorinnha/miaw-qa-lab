"""Spec 02 §3: parse Unity stack traces into frames and keep only the game's own frames."""

from __future__ import annotations

import re
from collections.abc import Iterable
from dataclasses import dataclass

# "QALab.Sandbox.SeededDoor.Open () (at Assets/Sandbox/Scripts/SeededBugs/SeededDoor.cs:27)"
#   → qual="QALab.Sandbox.SeededDoor" sep="." method="Open" args="" file="Assets/...cs" line="27"
# "QALab.Sandbox.SeededInventory:GetSlot (int) (at Assets/.../SeededInventory.cs:33)"
#   → qual="QALab.Sandbox.SeededInventory" sep=":" method="GetSlot" args="int" ...
# "System.Collections.Generic.Dictionary`2[TKey,TValue].get_Item (TKey key) (at <b2e5…>:0)"
#   → qual="System.Collections.Generic.Dictionary`2[TKey,TValue]" method="get_Item" file="<b2e5…>"
_FRAME = re.compile(
    r"^(?P<qual>[\w.`\[\],<>+]+?)(?P<sep>[.:])(?P<method>[\w<>`]+) ?\((?P<args>[^)]*)\)"
    r"(?: \(at (?P<file>.+?):(?P<line>\d+)\))?$"
)

# Frames from the engine, the runtime or our own bot never shape a game bug's signature.
ENGINE_PREFIXES = ("UnityEngine.", "UnityEditor.", "System.", "Mono.", "MiawWorks.QALab.")


@dataclass(frozen=True)
class Frame:
    qualified: str  # "Namespace.Class.Method" (":" normalized to ".")
    file: str | None = None  # "Assets/.../SeededDoor.cs", None when unknown
    line: int | None = None  # None when unknown or "(at <hash>:0)"

    @property
    def is_app(self) -> bool:
        return not self.qualified.startswith(ENGINE_PREFIXES)

    @property
    def class_name(self) -> str:
        """Last class segment: "QALab.Sandbox.SeededDoor.Open" → "SeededDoor"."""
        parts = self.qualified.split(".")
        return parts[-2] if len(parts) >= 2 else self.qualified

    @property
    def method(self) -> str:
        return self.qualified.rsplit(".", 1)[-1]

    def __str__(self) -> str:
        if self.file and self.line is not None:
            return f"{self.qualified} ({self.file}:{self.line})"
        return self.qualified


def parse_frame(line: str) -> Frame | None:
    """One stripped stack line → Frame, or None when it isn't a frame."""
    match = _FRAME.match(line.strip())
    if not match:
        return None
    qualified = f"{match.group('qual')}.{match.group('method')}"
    file, line_no = match.group("file"), match.group("line")
    # "(at <b2e5…>:0)" is an IL frame with no source location.
    if file is None or file.startswith("<") or line_no == "0":
        return Frame(qualified)
    return Frame(qualified, file, int(line_no))


def parse_stack(stack: str | None) -> list[Frame]:
    """All frames in order, skipping lines that don't parse (e.g. a message line)."""
    if not stack:
        return []
    frames = (parse_frame(line) for line in stack.splitlines())
    return [f for f in frames if f is not None]


def app_frames(frames: Iterable[Frame]) -> list[Frame]:
    """Only the game's own frames: engine, runtime and QALab bot frames are dropped."""
    return [f for f in frames if f.is_app]


def app_frames_of(stack: str | None) -> list[Frame]:
    return app_frames(parse_stack(stack))
