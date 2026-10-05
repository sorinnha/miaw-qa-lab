"""``qalab`` command line (typer). The only module that prints, through rich."""

from __future__ import annotations

import logging
import os
from pathlib import Path
from typing import Annotated

import typer
from rich.console import Console
from rich.table import Table

from qalab.config import NO_DOTENV_ENV, Config, load_config, load_dotenv
from qalab.io.runs import discover_runs, validate_labels_file, validate_run
from qalab.llm.base import LLMError, LLMProvider
from qalab.llm.factory import make_provider
from qalab.report.html import rerender_html
from qalab.triage.cluster import VARIANTS
from qalab.triage.pipeline import TriageOptions, load_runs, run_triage, triage_clusters
from qalab.vision.findings import validate_findings_file

app = typer.Typer(no_args_is_help=True, help="Miaw QA Lab tools.")
triage_app = typer.Typer(no_args_is_help=True, help="From run folders to ranked bug reports.")
report_app = typer.Typer(no_args_is_help=True, help="Re-render report files.")
eval_app = typer.Typer(
    no_args_is_help=True, help="Score triage and vision against seeded ground truth."
)
vision_app = typer.Typer(no_args_is_help=True, help="Find visual bugs in screenshots (spec 03).")
app.add_typer(triage_app, name="triage")
app.add_typer(report_app, name="report")
app.add_typer(eval_app, name="eval")
app.add_typer(vision_app, name="vision")
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
    """Schema-check run.json, events.jsonl (and labels.json, visual_findings.jsonl if present)."""
    runs = discover_runs(run_dirs)
    if not runs:
        console.print("[red]no run folders found[/red]")
        raise typer.Exit(EXIT_ERROR)
    failed = False
    for run_dir in runs:
        report = validate_run(run_dir)
        labels_error = validate_labels_file(run_dir)
        findings_errors = validate_findings_file(run_dir)
        ok = report.ok and labels_error is None and not findings_errors
        failed |= not ok
        status = "[green]ok[/green]" if ok else "[red]FAIL[/red]"
        console.print(f"{status} {run_dir}: {report.valid_events} events")
        if report.run_json_error:
            console.print(f"  run.json: {report.run_json_error}")
        for bad in report.invalid[:20]:
            console.print(f"  line {bad.line}: {bad.error}")
        for error in findings_errors[:20]:
            console.print(f"  visual_findings.jsonl {error}")
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
    from qalab.eval.triage_eval import run_triage_eval, safe_label, to_markdown, write_result

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
    try:
        options = _options(
            out / f"reports_{safe_label(label)}",
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
    # soft_wrap: long table rows stay on one line, so the printed Markdown can be pasted as is.
    console.print(to_markdown(result), soft_wrap=True, markup=False, highlight=False)
    console.print("wrote " + ", ".join(str(p) for p in written))


def _vision_provider(
    name: str | None, model: str | None, no_cache: bool, config_path: Path | None
) -> tuple[Config, LLMProvider | None]:
    """The provider for vision commands: --provider, QALAB_PROVIDER or qalab.toml, cache-wrapped."""
    from qalab.triage.prompts import load_prompt

    config = load_config(config_path)
    chosen = (name or os.environ.get("QALAB_PROVIDER") or config.llm.provider).lower()
    same = chosen == config.llm.provider.lower()
    return config, make_provider(
        chosen,
        model or os.environ.get("QALAB_MODEL") or (config.llm.model if same else None),
        use_cache=not no_cache,
        prompt_version=load_prompt("vision_v1").version,
    )


@vision_app.command("analyze")
def vision_analyze(
    run_dirs: Annotated[list[Path], typer.Argument(help="Run folders or globs.")],
    method: Annotated[
        str | None,
        typer.Option("--method", help="heuristic | vlm | ml | hybrid (default: qalab.toml)"),
    ] = None,
    provider: ProviderOpt = None,
    model: Annotated[str | None, typer.Option("--model")] = None,
    ml_model: Annotated[
        Path | None, typer.Option("--ml-model", help="Model from qalab vision train-ml.")
    ] = None,
    no_cache: Annotated[bool, typer.Option("--no-cache")] = False,
    config: ConfigOpt = None,
) -> None:
    """Label every screenshot; writes visual_findings.jsonl into each run folder."""
    from qalab.vision.analyze import METHODS, AnalyzeOptions, analyze_run
    from qalab.vision.heuristics import Thresholds
    from qalab.vision.ml import MlModel

    try:
        cfg = load_config(config)
        chosen = (method or cfg.vision.method).lower()
        if chosen not in METHODS:
            raise ValueError(f"--method must be one of {', '.join(METHODS)}, not {chosen!r}")
        llm = None
        if chosen in ("vlm", "hybrid"):
            cfg, llm = _vision_provider(provider, model, no_cache, config)
        options = AnalyzeOptions(
            method=chosen,  # type: ignore[arg-type]
            provider=llm,
            ml_model=MlModel.load(ml_model) if ml_model else None,
            thresholds=Thresholds(
                black_ratio=cfg.vision.black_ratio, magenta_ratio=cfg.vision.magenta_ratio
            ),
            hybrid_every_n=cfg.vision.hybrid_every_n,
            hybrid_window_s=cfg.vision.hybrid_window_s,
            max_retries=cfg.llm.max_retries,
        )
        runs = load_runs(run_dirs)
        summaries = [analyze_run(loaded, options) for loaded in runs.values()]
    except USER_ERRORS as exc:
        console.print(f"[red]error:[/red] {exc}")
        raise typer.Exit(EXIT_ERROR) from exc
    for s in summaries:
        found = ", ".join(f"{label}={n}" for label, n in sorted(s.labels.items())) or "nothing"
        extra = f", {s.vlm_calls} VLM calls ({s.vlm_errors} failed)" if s.vlm_calls else ""
        missing = f", {s.missing} missing files" if s.missing else ""
        console.print(f"{s.run_id}: {s.frames} frames, found {found}{extra}{missing} -> {s.path}")


@vision_app.command("dataset")
def vision_dataset(
    run_dirs: Annotated[list[Path], typer.Argument(help="Benchmark run folders or globs.")],
    out: Annotated[Path, typer.Option("--out", help="Dataset folder.")] = Path(
        "datasets/vision_v1"
    ),
    seed: Annotated[int, typer.Option("--seed", help="Seed for the run split.")] = 7,
) -> None:
    """Build the image dataset (split 70/15/15 by run) from benchmark runs and their labels."""
    from qalab.eval.vision_dataset import build_dataset, dataset_stats

    try:
        frames = build_dataset(run_dirs, out, seed)
    except USER_ERRORS as exc:
        console.print(f"[red]error:[/red] {exc}")
        raise typer.Exit(EXIT_ERROR) from exc
    stats = dataset_stats(frames, seed)
    for split, part in stats["splits"].items():  # type: ignore[union-attr]
        labels = ", ".join(f"{k}={v}" for k, v in part["labels"].items())
        console.print(f"{split}: {part['frames']} frames from {part['runs']} runs, {labels}")
    for warning in stats["warnings"]:  # type: ignore[union-attr]
        console.print(f"[yellow]warning:[/yellow] {warning}")
    console.print(f"wrote {out / 'index.csv'}")


@vision_app.command("train-ml")
def vision_train_ml(
    dataset_dir: Annotated[Path, typer.Argument(help="Folder from qalab vision dataset.")],
    out: Annotated[Path | None, typer.Option("--out", help="Model file.")] = None,
    seed: Annotated[int, typer.Option("--seed")] = 7,
) -> None:
    """Optional baseline: train the logistic-regression model on train, thresholds on val."""
    import numpy as np

    from qalab.eval.vision_dataset import load_dataset
    from qalab.vision.images import load_rgb
    from qalab.vision.ml import features, train

    try:
        frames = load_dataset(dataset_dir)
        part = {s: [f for f in frames if f.split == s] for s in ("train", "val")}
        if not part["train"]:
            raise ValueError("the dataset has no train split")
        x = {
            s: np.array([features(load_rgb(dataset_dir / f.path)) for f in v])
            for s, v in part.items()
            if v
        }
        y = {s: [f.labels for f in v] for s, v in part.items()}
        model = train(
            x["train"], y["train"], x.get("val", x["train"]), y["val"] or y["train"], seed
        )
        target = out or dataset_dir / "ml_model.joblib"
        model.save(target)
    except USER_ERRORS as exc:
        console.print(f"[red]error:[/red] {exc}")
        raise typer.Exit(EXIT_ERROR) from exc
    thresholds = ", ".join(f"{k}={v}" for k, v in model.thresholds.items())
    console.print(f"trained on {len(part['train'])} frames; thresholds {thresholds} -> {target}")


@eval_app.command("vision")
def eval_vision(
    dataset_dir: Annotated[Path, typer.Argument(help="Folder from qalab vision dataset.")],
    methods: Annotated[
        str, typer.Option("--methods", help="Comma-separated: heuristic,vlm,ml,hybrid.")
    ] = "heuristic,vlm,hybrid",
    provider: ProviderOpt = None,
    model: Annotated[str | None, typer.Option("--model")] = None,
    label: Annotated[
        str, typer.Option("--label", help="Name of this run in the output files.")
    ] = "default",
    no_cache: Annotated[bool, typer.Option("--no-cache")] = False,
    out: Annotated[Path, typer.Option("--out", help="Output folder.")] = Path("eval"),
    config: ConfigOpt = None,
) -> None:
    """Per-label P/R/F1, macro F1, FP/100 frames, VLM calls and latency per method (H1–H4)."""
    from qalab.eval.vision_eval import METHODS as EVAL_METHODS
    from qalab.eval.vision_eval import VisionEvalOptions, evaluate_vision, to_markdown, write_result
    from qalab.vision.heuristics import Thresholds

    chosen = [m.strip() for m in methods.split(",") if m.strip()]
    unknown = [m for m in chosen if m not in EVAL_METHODS]
    if unknown:
        console.print(
            f"[red]error:[/red] unknown method(s) {', '.join(unknown)} "
            f"(use {', '.join(EVAL_METHODS)})"
        )
        raise typer.Exit(EXIT_ERROR)
    try:
        cfg = load_config(config)
        llm = None
        if {"vlm", "hybrid"} & set(chosen):
            cfg, llm = _vision_provider(provider, model, no_cache, config)
        options = VisionEvalOptions(
            methods=chosen,
            provider=llm,
            cost_per_1k_images=cfg.vision.cost_per_1k_images,
            max_retries=cfg.llm.max_retries,
            configured=Thresholds(
                black_ratio=cfg.vision.black_ratio, magenta_ratio=cfg.vision.magenta_ratio
            ),
        )
        result = evaluate_vision(dataset_dir, options)
        written = write_result(result, dataset_dir, out, label)
    except USER_ERRORS as exc:
        console.print(f"[red]error:[/red] {exc}")
        raise typer.Exit(EXIT_ERROR) from exc
    console.print(to_markdown(result), soft_wrap=True, markup=False, highlight=False)
    console.print("wrote " + ", ".join(str(p) for p in written))
