"""CI smoke test (spec 04): validate the sample run, then run triage with the fake provider.

Cross-platform on purpose (the CI matrix has Windows and Ubuntu). Exit codes follow spec 02:
0 and 3 (a P1 bug was found) both count as a successful smoke run; anything else fails CI.

The triage command matches spec 04 (no ``--docs``), so only ``normalize_message`` gates it.
One exception, until Sora writes that YOU WRITE function: a run that stops on
``NotImplementedError("YOU WRITE")`` prints a GitHub warning and exits 0, so CI stays green
while the learning task is still open (docs/DECISIONS.md D-015).
"""

from __future__ import annotations

import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
SAMPLE_RUN = REPO / "samples" / "sample_run"
OUT = REPO / "out" / "ci_report"
ACCEPTED_EXIT_CODES = (0, 3)
TIMEOUT_S = 300
# Last line of both plain and rich tracebacks when a YOU WRITE stub is hit.
OPEN_YOU_WRITE = "NotImplementedError: YOU WRITE"


def run(args: list[str]) -> subprocess.CompletedProcess[str]:
    print("$", " ".join(args), flush=True)
    return subprocess.run(
        args, text=True, encoding="utf-8", capture_output=True, check=False, timeout=TIMEOUT_S
    )


def main() -> int:
    validate = run([sys.executable, "-m", "qalab", "validate", str(SAMPLE_RUN)])
    print(validate.stdout, validate.stderr, sep="")
    if validate.returncode != 0:
        print("::error::qalab validate failed on samples/sample_run")
        return validate.returncode

    shutil.rmtree(OUT, ignore_errors=True)  # a stale report must not pass the file check
    triage = run(
        [
            sys.executable,
            "-m",
            "qalab",
            "triage",
            "run",
            str(SAMPLE_RUN),
            "--provider",
            "fake",
            "--out",
            str(OUT),
            "--no-cache",
        ]
    )
    print(triage.stdout, triage.stderr, sep="")
    if triage.returncode in ACCEPTED_EXIT_CODES:
        missing = [n for n in ("bugs.json", "report.html") if not (OUT / n).is_file()]
        if missing:
            print(f"::error::smoke run wrote no {', '.join(missing)}")
            return 1
        print(f"smoke ok: exit {triage.returncode}, outputs in {OUT}")
        return 0
    if OPEN_YOU_WRITE in triage.stderr or OPEN_YOU_WRITE in triage.stdout:
        print("::warning::triage smoke skipped: a YOU WRITE task is still open (see PLAN.md)")
        return 0
    print(f"::error::qalab triage run exited {triage.returncode}")
    return triage.returncode or 1


if __name__ == "__main__":
    sys.exit(main())
