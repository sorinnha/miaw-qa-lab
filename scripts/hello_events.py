"""YOU WRITE (M0, about 25 lines): count events per ``kind`` and log lines per ``level``.

Run it from the repo root:

    python scripts/hello_events.py                      # samples/sample_run/events.jsonl
    python scripts/hello_events.py runs/<run_id>/events.jsonl

Expected output for the sample run (38 events)::

    kind   action      9
    kind   detector    2
    ...
    level  warning     3

Hints: open the file with ``path.open(encoding="utf-8")`` and loop ``for line in f`` (a
``StreamReader.ReadLine()`` loop in C#); ``json.loads(line)`` gives a dict; count with
``collections.Counter`` (like ``Dictionary<string,int>`` with a built-in ``+1``). Skip blank
lines. ``level`` only exists on ``kind == "log"`` events.
"""

from __future__ import annotations

import sys
from collections import Counter
from pathlib import Path

DEFAULT_EVENTS = (
    Path(__file__).resolve().parents[1] / "samples" / "sample_run" / "events.jsonl"
)


def count_events(events_path: Path) -> tuple[Counter[str], Counter[str]]:
    """Return ``(per_kind, per_level)`` counters for one ``events.jsonl`` file.

    ``per_kind`` counts every event by its ``kind``; ``per_level`` counts only ``log`` events
    by their ``level``. Blank lines are skipped. Must not load the whole file into memory.
    """
    raise NotImplementedError("YOU WRITE")


def main(argv: list[str] | None = None) -> int:
    """Print the two tables, sorted by name, as ``kind <name> <count>`` / ``level <name> <count>``."""
    args = sys.argv[1:] if argv is None else argv
    events_path = Path(args[0]) if args else DEFAULT_EVENTS
    per_kind, per_level = count_events(events_path)
    for name, count in sorted(per_kind.items()):
        print(f"kind   {name:<11}{count:>3}")
    for name, count in sorted(per_level.items()):
        print(f"level  {name:<11}{count:>3}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
