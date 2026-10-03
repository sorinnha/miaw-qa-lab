"""``qalab`` command line (typer). The only module that prints, through rich."""

from __future__ import annotations

import logging
from pathlib import Path
from typing import Annotated

import typer
from rich.console import Console

from qalab.io.runs import discover_runs, validate_labels_file, validate_run

app = typer.Typer(no_args_is_help=True, help="Miaw QA Lab tools.")
console = Console()

EXIT_OK = 0
EXIT_ERROR = 2
EXIT_P1 = 3


@app.callback()
def _setup(verbose: Annotated[bool, typer.Option("--verbose", "-v")] = False) -> None:
    logging.basicConfig(level=logging.DEBUG if verbose else logging.WARNING)


@app.command()
def validate(
    run_dirs: Annotated[list[Path], typer.Argument(help="Run folders or globs.")],
) -> None:
    """Schema-check run.json, events.jsonl (and labels.json if present)."""
    runs = discover_runs(run_dirs)
    if not runs:
        console.print("[red]no run folders found[/red]")
        raise typer.Exit(EXIT_ERROR)
    failed = False
    for run_dir in runs:
        report = validate_run(run_dir)
        labels_error = validate_labels_file(run_dir)
        ok = report.ok and labels_error is None
        failed |= not ok
        status = "[green]ok[/green]" if ok else "[red]FAIL[/red]"
        console.print(f"{status} {run_dir}: {report.valid_events} events")
        if report.run_json_error:
            console.print(f"  run.json: {report.run_json_error}")
        for bad in report.invalid[:20]:
            console.print(f"  line {bad.line}: {bad.error}")
        if labels_error:
            console.print(f"  labels.json: {labels_error}")
    raise typer.Exit(EXIT_ERROR if failed else EXIT_OK)
