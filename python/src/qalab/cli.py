"""``qalab`` command line (typer). The only module that prints, through rich."""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Annotated

import typer
from rich.console import Console
from rich.table import Table

from qalab.config import NO_DOTENV_ENV, load_config, load_dotenv
from qalab.io.runs import discover_runs, validate_labels_file, validate_run
from qalab.llm.base import LLMError
from qalab.llm.factory import make_provider
from qalab.report.html import rerender_html
from qalab.triage.cluster import VARIANTS
from qalab.triage.pipeline import TriageOptions, load_runs, run_triage, triage_clusters

app = typer.Typer(no_args_is_help=True, help="Miaw QA Lab tools.")
triage_app = typer.Typer(no_args_is_help=True, help="From run folders to ranked bug reports.")
report_app = typer.Typer(no_args_is_help=True, help="Re-render report files.")
eval_app = typer.Typer(
    no_args_is_help=True, help="Score triage and vision against seeded ground truth."
)
app.add_typer(triage_app, name="triage")
app.add_typer(report_app, name="report")
app.add_typer(eval_app, name="eval")
console = Console()

EXIT_OK = 0
EXIT_ERROR = 2
EXIT_P1 = 3

ConfigOpt = Annotated[Path | None, typer.Option("--config", help="Path to qalab.toml.")]
ClusterOpt = Annotated[
    str, typer.Option("--cluster", help="exact | frame_tfidf | frame_embed | tfidf_only")
]
# Errors the CLI reports as exit 2 instead of a traceback (spec 02: 2 = error).
USER_ERRORS = (FileNotFoundError, ValueError, LLMError)

ProviderOpt = Annotated[
    str | None,
    typer.Option("--provider", help="ollama | gemini | openai | anthropic | fake | none"),
]


@app.callback()
def _setup(verbose: Annotated[bool, typer.Option("--verbose", "-v")] = False) -> None:
    logging.basicConfig(level=logging.INFO if verbose else logging.WARNING)
    # Keys and model names may live in .env (gitignored). Tests set QALAB_NO_DOTENV so a real key in
    # a developer's .env is never read during pytest.
    if not os.environ.get(NO_DOTENV_ENV):
        load_dotenv(Path(".env"))


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


def _options(
    out: Path,
    provider: str | None,
    model: str | None,
    cluster: str,
    max_reports: int | None,
    docs: list[Path] | None,
    repo: Path | None,
    no_cache: bool,
    config_path: Path | None,
) -> TriageOptions:
    config = load_config(config_path)
    if cluster not in VARIANTS:
        raise ValueError(f"--cluster must be one of {', '.join(VARIANTS)}, not {cluster!r}")
    name = (provider or os.environ.get("QALAB_PROVIDER") or config.llm.provider).lower()
    # qalab.toml's model names belong to qalab.toml's provider: a Gemini model name must not be sent
    # to Ollama when someone runs --provider ollama.
    same_provider = name == config.llm.provider.lower()
    return TriageOptions(
        out=out,
        provider=name,
        model=model
        or os.environ.get("QALAB_MODEL")
        or (config.llm.model if same_provider else None),
        embed_model=os.environ.get("QALAB_EMBED_MODEL")
        or (config.llm.embed_model if same_provider else None),
        variant=cluster,  # type: ignore[arg-type]
        max_reports=max_reports or config.triage.max_reports,
        docs=list(docs or []),
        repo=repo,
        use_cache=not no_cache,
        config=config,
    )


@triage_app.command("run")
def triage_run(
    run_dirs: Annotated[list[Path], typer.Argument(help="Run folders or globs.")],
    out: Annotated[Path, typer.Option("--out", help="Output folder.")],
    docs: Annotated[list[Path] | None, typer.Option("--docs", help="Markdown design docs.")] = None,
    repo: Annotated[
        Path | None, typer.Option("--repo", help="Game source for code context.")
    ] = None,
    provider: ProviderOpt = None,
    model: Annotated[str | None, typer.Option("--model")] = None,
    cluster: ClusterOpt = "frame_tfidf",
    max_reports: Annotated[int | None, typer.Option("--max-reports")] = None,
    no_cache: Annotated[bool, typer.Option("--no-cache")] = False,
    config: ConfigOpt = None,
) -> None:
    """Full pipeline: cluster, rank, write reports. Exit 3 when a P1 bug was found."""
    try:
        options = _options(out, provider, model, cluster, max_reports, docs, repo, no_cache, config)
        result = run_triage(run_dirs, options)
    except USER_ERRORS as exc:
        console.print(f"[red]error:[/red] {exc}")
        raise typer.Exit(EXIT_ERROR) from exc
    by_priority = result.meta["by_priority"]
    console.print(
        f"{len(result.reports)} reports from {len(result.clusters)} clusters -> {out}  "
        + "  ".join(f"{p}={n}" for p, n in by_priority.items())
    )
    raise typer.Exit(result.exit_code)


@triage_app.command("clusters")
def triage_clusters_cmd(
    run_dirs: Annotated[list[Path], typer.Argument(help="Run folders or globs.")],
    cluster: ClusterOpt = "frame_tfidf",
    provider: ProviderOpt = None,
    config: ConfigOpt = None,
) -> None:
    """Debug: print clusters and scores without writing reports (no LLM unless frame_embed)."""
    try:
        options = _options(Path("."), provider, None, cluster, None, None, None, True, config)
        llm = None
        if cluster == "frame_embed":  # the only variant that needs a model (embeddings)
            llm = make_provider(options.provider, options.model, options.embed_model, False)
        runs = load_runs(run_dirs)
        ranked = triage_clusters(runs, options, llm)
    except USER_ERRORS as exc:
        console.print(f"[red]error:[/red] {exc}")
        raise typer.Exit(EXIT_ERROR) from exc
    table = Table(title=f"{len(runs)} run(s), variant {cluster}")
    for column in ("Prio", "Score", "Kind", "Signature", "Count", "Label", "Top frame"):
        table.add_column(column)
    for c in ranked:
        frame = c.top_frame.qualified if c.top_frame else "-"
        table.add_row(c.priority, f"{c.score:g}", c.kind, c.signature, str(c.count), c.label, frame)
    console.print(table)


@report_app.command("html")
def report_html(
    reports_dir: Annotated[Path, typer.Argument(help="Folder with bugs.json.")],
) -> None:
    """Re-render report.html from bugs.json."""
    if not (reports_dir / "bugs.json").is_file():
        console.print(f"[red]no bugs.json in {reports_dir}[/red]")
        raise typer.Exit(EXIT_ERROR)
    target = rerender_html(reports_dir)
    console.print(f"wrote {target}")


@eval_app.command("triage")
def eval_triage(
    benchmark_dir: Annotated[
        Path, typer.Argument(help="Folder of benchmark runs (with labels.json).")
    ],
    variants: Annotated[
        str, typer.Option("--variants", help="E1: comma-separated clustering variants, or 'none'.")
    ] = ",".join(VARIANTS),
    reports: Annotated[
        bool, typer.Option("--reports/--no-reports", help="E2/E3: also write and score reports.")
    ] = False,
    label: Annotated[
        str, typer.Option("--label", help="Name of this setting in the output files.")
    ] = "default",
    docs: Annotated[list[Path] | None, typer.Option("--docs", help="Design docs (RAG on).")] = None,
    design_doc: Annotated[
        Path | None,
        typer.Option(
            "--design-doc",
            help="Design doc for scoring 'component correct' only, never sent to the model "
            "(default: the first --docs file). Lets RAG-off runs be scored too.",
        ),
    ] = None,
    repo: Annotated[
        Path | None, typer.Option("--repo", help="Game source for code context.")
    ] = None,
    provider: ProviderOpt = None,
    model: Annotated[str | None, typer.Option("--model")] = None,
    report_variant: ClusterOpt = "frame_tfidf",
    max_reports: Annotated[int | None, typer.Option("--max-reports")] = None,
    no_cache: Annotated[bool, typer.Option("--no-cache")] = False,
    out: Annotated[Path, typer.Option("--out", help="Output folder.")] = Path("eval"),
    config: ConfigOpt = None,
) -> None:
    """Clustering P/R/F1 per variant (E1) and, with --reports, report quality (E2, E3)."""
    from qalab.eval.triage_eval import run_triage_eval, to_markdown, write_result

    chosen = (
        []
        if variants.strip().lower() == "none"
        else [v.strip() for v in variants.split(",") if v.strip()]
    )
    unknown = [v for v in chosen if v not in VARIANTS]
    if unknown:
        console.print(
            f"[red]error:[/red] unknown variant(s) {', '.join(unknown)} (use {', '.join(VARIANTS)})"
        )
        raise typer.Exit(EXIT_ERROR)
    safe_label = "".join(c if c.isalnum() or c in "_.-" else "_" for c in label)
    try:
        options = _options(
            out / f"reports_{safe_label}",
            provider,
            model,
            report_variant,
            max_reports,
            docs,
            repo,
            no_cache,
            config,
        )
        design_doc = design_doc or (docs[0] if docs else None)
        result = run_triage_eval(benchmark_dir, chosen, options, reports, label, design_doc)  # type: ignore[arg-type]
        written = write_result(result, out, label)
    except USER_ERRORS as exc:
        console.print(f"[red]error:[/red] {exc}")
        raise typer.Exit(EXIT_ERROR) from exc
    console.print(to_markdown(result))
    console.print("wrote " + ", ".join(str(p) for p in written))
