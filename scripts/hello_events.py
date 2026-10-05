"""Learning task (M0), written by Claude at Sora's request (D-030): count events per ``kind`` and
log lines per ``level``.

Run it from the repo root:

    python scripts/hello_events.py                      # samples/sample_run/events.jsonl
    python scripts/hello_events.py runs/<run_id>/events.jsonl

Expected output for the sample run (38 events), exactly::

    kind   action       9
    kind   detector     2
    kind   log         12
    kind   marker       5
    kind   metric       5
    kind   screenshot   5
    level  error        4
    level  exception    4
    level  info         1
    level  warning      3

Each line is ``f"kind   {name:<11}{count:>3}"`` (or ``level  ...``): the name padded to 11
characters, the count right-aligned in 3. Kinds first, then levels, each sorted by name.

How it works:
- ``json.loads(line)`` turns one line into a dict. The file is read with ``for line in f``, one
  line at a time (like a ``StreamReader.ReadLine()`` loop in C#). Blank lines are skipped, and so is
  a line that isn't JSON (a crashed run can end with half a line): it is reported on stderr.
- ``Counter()`` from ``collections`` is a ``Dictionary<string, int>`` where a missing key starts at
  0, so ``per_kind[event["kind"]] += 1`` just works.
- ``level`` only exists on events whose ``kind`` is ``"log"``.
- ``sorted(counter.items())`` gives ``(name, count)`` pairs in name order.
"""

from __future__ import annotations

import json
import sys
from collections import Counter
from pathlib import Path

DEFAULT_EVENTS = Path(__file__).resolve().parents[1] / "samples" / "sample_run" / "events.jsonl"


def count_events(events_path: Path) -> tuple[Counter[str], Counter[str]]:
    """Return ``(per_kind, per_level)`` counters for one ``events.jsonl`` file.

    ``per_kind`` counts every event by its ``kind``; ``per_level`` counts only ``log`` events
    by their ``level``. Blank lines are skipped; a line that isn't JSON is skipped and reported on
    stderr. Read line by line, not the whole file at once.
    """
    per_kind: Counter[str] = Counter()
    per_level: Counter[str] = Counter()
    with events_path.open(encoding="utf-8") as f:
        # enumerate gives (line number, line); start=1 so the numbers match an editor's.
        for number, line in enumerate(f, start=1):
            if not line.strip():
                continue
            try:
                event = json.loads(line)
            except json.JSONDecodeError:
                print(f"skipped line {number}: not JSON", file=sys.stderr)
                continue
            per_kind[event["kind"]] += 1
            if event["kind"] == "log":
                per_level[event["level"]] += 1
    return per_kind, per_level


def main(argv: list[str] | None = None) -> int:
    """Count ``argv[0]`` (default: the sample run), print both tables as shown above, return 0.

    ``argv`` is the list of command-line arguments without the script name; when it is None,
    use ``sys.argv[1:]``.
    """
    args = sys.argv[1:] if argv is None else argv
    path = Path(args[0]) if args else DEFAULT_EVENTS
    per_kind, per_level = count_events(path)
    for name, count in sorted(per_kind.items()):
        print(f"kind   {name:<11}{count:>3}")
    for name, count in sorted(per_level.items()):
        print(f"level  {name:<11}{count:>3}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
