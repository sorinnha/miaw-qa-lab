"""Spec 02 §10: show the source lines around the top app frame when ``--repo`` is given."""

from __future__ import annotations

import re
from pathlib import Path

from qalab.triage.stack import Frame

RADIUS = 8
MAX_LINES = 40
MAX_GLOB_DEPTH = 3


def resolve_source(file: str, repo: Path) -> Path | None:
    """``Assets/.../SeededDoor.cs`` → that file under ``repo`` or under any ``*/Assets/...``."""
    relative = Path(file.replace("\\", "/"))
    direct = repo / relative
    if direct.is_file():
        return direct
    for depth in range(1, MAX_GLOB_DEPTH + 1):
        pattern = "/".join(["*"] * depth) + "/" + relative.as_posix()
        for match in sorted(repo.glob(pattern)):
            if match.is_file():
                return match
    return None


def find_line(lines: list[str], class_name: str, method: str) -> int | None:
    """1-based line of ``{method}(`` after ``class {Class}``, for frames without a line number."""
    # "public class SeededDoor : MonoBehaviour" → matches class SeededDoor
    class_re = re.compile(rf"\bclass\s+{re.escape(class_name)}\b")
    # "    public void Open()" → matches Open(
    method_re = re.compile(rf"\b{re.escape(method)}\s*\(")
    start = next((i for i, line in enumerate(lines) if class_re.search(line)), None)
    if start is None:
        return None
    for i in range(start, len(lines)):
        if method_re.search(lines[i]):
            return i + 1
    return start + 1


def snippet(path: Path, line: int, radius: int = RADIUS, max_lines: int = MAX_LINES) -> str:
    """Numbered lines ``line ± radius`` (capped at ``max_lines``); the frame line gets ``>``."""
    lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    if not lines:
        return ""
    line = min(max(line, 1), len(lines))
    first = max(1, line - radius)
    last = min(len(lines), line + radius, first + max_lines - 1)
    width = len(str(last))
    out = [f"{path.as_posix()}:{line}"]
    for number in range(first, last + 1):
        marker = ">" if number == line else " "
        out.append(f"{marker}{number:>{width}} | {lines[number - 1]}")
    return "\n".join(out)


def code_context_for(frame: Frame | None, repo: Path | None) -> str | None:
    """The CODE block for a frame, or None when there is nothing to show."""
    if frame is None or repo is None or not frame.file:
        return None
    path = resolve_source(frame.file, repo)
    if path is None:
        return None
    line = frame.line
    if line is None:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        line = find_line(lines, frame.class_name, frame.method)
        if line is None:
            return None
    return snippet(path, line)
