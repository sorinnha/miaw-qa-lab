"""``bugs_jira.csv``: one row per bug, importable into Jira."""

from __future__ import annotations

import csv
from collections.abc import Iterable
from pathlib import Path

from qalab.models.bug import BugReport

COLUMNS = ["Summary", "Description", "Issue Type", "Priority", "Labels", "Component"]
PRIORITY_NAMES = {"P1": "Highest", "P2": "High", "P3": "Medium", "P4": "Low"}


def jira_description(r: BugReport) -> str:
    steps = "\n".join(f"# {s.text}" for s in r.steps_to_reproduce)
    evidence = ", ".join(f"{e.run_id}#{e.seq}" for e in r.evidence)
    return (
        f"{r.summary}\n\n*Steps to reproduce*\n{steps}\n\n*Expected*\n{r.expected}\n\n"
        f"*Actual*\n{r.actual}\n\n*Suspected cause*\n{r.suspected_cause}\n\n"
        f"*Evidence*\n{evidence}\n\nQA Lab id: {r.id} · signature {r.signature}"
        f" · score {r.score:g} · severity {r.severity}"
    )


def write_jira_csv(path: Path, reports: Iterable[BugReport]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(COLUMNS)
        for r in reports:
            writer.writerow(
                [
                    r.title,
                    jira_description(r),
                    "Bug",
                    PRIORITY_NAMES[r.priority],
                    f"qalab;{r.kind}",
                    r.component,
                ]
            )
