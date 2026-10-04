# Miaw QA Lab

[![python-ci](https://github.com/sorinnha/miaw-qa-lab/actions/workflows/python-ci.yml/badge.svg)](https://github.com/sorinnha/miaw-qa-lab/actions/workflows/python-ci.yml)

AI-assisted game QA for Unity. A seeded autoplay bot explores your game and catches bugs, and a Python tool turns the logs and screenshots into ranked, evidence-backed bug reports.

> **Status:** in development. See [docs/PLAN.md](docs/PLAN.md) for the roadmap and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

- **Unity package** (`unity/com.miawworks.qalab`, C#): event logging, metrics, screenshots, a seeded bot (NavMesh explorer, UI crawler, game adapters), and detectors for falls, stuck players, frame spikes and tunnelling.
- **`qalab` CLI** (`python/`): deduplicates and ranks errors, then drafts Jira-ready reports with a local LLM and RAG over design docs and code. Every claim cites evidence.
- **Vision:** finds missing textures, black screens and broken UI using heuristics, a VLM, or both.
- **Benchmark:** 16 seeded bugs in a sandbox project, so every result is measured.

## Quick start (triage)

```powershell
py -3.12 -m venv .venv ; .\.venv\Scripts\Activate.ps1 ; pip install -e ".\python[dev]"
qalab validate samples\sample_run
qalab triage run samples\sample_run --provider fake --docs docs\sandbox_design.md --out out\sample_report
```

Linux/macOS: `python3 -m venv .venv && . .venv/bin/activate && pip install -e "./python[dev]"`, then the same `qalab` commands with `/`.

`qalab triage run` writes `bugs.json`, `report.md`, `bugs_jira.csv`, a self-contained `report.html`, plus `clusters.json`, `validation_report.json` and `triage_meta.json`. Exit code 3 means a P1 bug was found (CI can fail on it). Providers: `ollama` (default, local), `fake` (deterministic, used by the tests), `none` (template reports, TF-IDF retrieval). Tunables live in `qalab.toml`.

Two functions are written by hand as learning tasks (`normalize_message`, `cosine_top_k`); until they exist the end-to-end run raises `NotImplementedError("YOU WRITE")` and their tests are expected failures (`pytest python -m youwrite -rxX`).

## Data handling

Triage sends log lines, stack traces, nearby bot actions, design-doc excerpts and (for vision)
screenshots to the configured model. Choose the provider with that in mind:

- **Gemini (configured in `qalab.toml`).** Sora's PC runs no local model, so triage uses Google's
  hosted Gemini API (`[gemini]` extra, key in `GEMINI_API_KEY`). On the free tier Google may use
  what you send to improve its products, and people may review it. **Send sandbox data only**:
  `samples/` and runs of `unity/QALabSandbox`. Don't send runs of an unreleased or proprietary game
  unless you're on a paid tier whose terms allow it, or switch that run to `--provider ollama` or
  `--provider none`.
- **Ollama** keeps everything on the machine; **`none`** writes template reports with no model;
  **`fake`** is the deterministic offline provider the tests use.
- The API key is read from the environment or `.env` (gitignored) only. It is never logged, cached
  or written to reports, and prompts are not logged either (`triage_meta.json` holds token counts
  and timings only). The LLM cache in `.cache/llm.sqlite` stores model answers; it is gitignored.

The rest of the README (demo, measured results, how it was built) is written in milestones M5 and M7. Every number there will come from a script in this repo.

License: MIT
