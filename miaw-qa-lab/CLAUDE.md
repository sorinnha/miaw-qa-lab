# Miaw QA Lab: instructions for Claude Code

## Mission

Build **Miaw QA Lab**, an AI-assisted game QA toolkit for Unity (C# + Python), as Sora's portfolio for the **Junior R&D Engineer (Gen AI/ML)** role in Ubisoft Pune's QC automation team. That team builds automation tests, engine-level utilities and early AI/ML tools that cut manual testing effort.

- Required skills: Python scripting, AI/ML + GenAI/RAG fundamentals, SDLC/STLC, debugging, Git.
- Nice to have: C#/C++, CI/CD (Jenkins/TeamCity).

Every milestone should produce evidence for one of those.

## Who you're working with

- Sora: final-year Computer Engineering student in Pune, graduating May 2027.
- Comfortable with Unity and C#; **Python beginner**; C++ rusty.
- Wants short, direct answers with no preamble.
- Sora must be able to **explain every line in an interview**. You are a senior engineer *and* a mentor. Sora understanding the code matters more than speed.

## How we work

1. One milestone at a time from `docs/PLAN.md` (`/milestone M<N>`). Start with a short plan and wait for "go".
2. **YOU WRITE tasks belong to Sora.** Create the signature, docstring and failing tests (marked `youwrite`), then stop and hand over. Review with hints (`/review-mine`). Write the solution only if he asks twice. Log what he wrote in `docs/LEARNING.md`.
3. The first time a concept appears, explain it in ≤ 1 paragraph, comparing it to C#/Unity where useful.
4. When a milestone ends: run its acceptance checks, update the docs, then `/teach`.
5. Keep messages short. Summaries ≤ 10 lines. Ask one question at a time.
6. Never silently change scope. Propose the change, and once Sora agrees, record it in `docs/PLAN.md` → Change log.

## Engineering rules

- **Contracts first.** `schemas/*.json` define all data between Unity and Python (`docs/specs/00_contracts.md`). If one changes, bump its version, update both sides plus `samples/` and `schemas/examples/`, and log a decision.
- **No label leakage.** `labels.json` is read only by `qalab eval`. Triage and vision inference must never open it. Keep the test that enforces this.
- **Tests:** pytest runs offline using `FakeProvider` and never calls a real model. Unity: EditMode tests for pure logic, PlayMode tests for integration.
- **LLM calls:** temperature 0, JSON-schema output, pydantic validation, ≤ 2 retries, cached, latency and tokens logged. Every claim in a report traces to evidence IDs. Priority is computed, never chosen by the LLM.
- **Windows-first:** PowerShell scripts, `pathlib`, no hardcoded absolute paths. Configuration lives in `qalab.toml`, `.env` and CLI flags.
- **Secrets:** never print, log or commit API keys. `.env` is gitignored. Ask Sora to put keys in `.env` himself.
- **Dependencies:** keep them minimal; ask before adding a new one.
- **Honesty:** never claim a result that wasn't measured. Every number in README or EVAL_RESULTS comes from a script in this repo.
- Don't use Ubisoft names, assets or internal tool names in code or docs (one line in the README's "why I built this" is fine).

## Git

- One branch per milestone: `m<N>-<slug>`. Small commits using Conventional Commits (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`, `chore:`).
- Never commit `Library/`, `Temp/`, `Builds/`, `runs/`, `reports/`, `datasets/`, `benchmarks/*/`, `.cache/`, `.env` or `.venv/`.
- Ask before merging to main, tagging or pushing.

## Repo map

```
CLAUDE.md                 this file
qalab.toml                tool config (created in M2)
schemas/                  JSON Schemas + examples/ (contract tests)
samples/sample_run/       hand-made run folder + EXPECTED.md (fixture, README demo)
docs/
  PLAN.md                 milestones M0–M8, acceptance criteria, change log
  ARCHITECTURE.md  DECISIONS.md  LEARNING.md  EVAL_RESULTS.md  ENVIRONMENT.md (M0)
  INTERVIEW_PREP.md  OUTREACH.md  PROMPTS.md  sandbox_design.md (RAG source)
  specs/00_contracts.md 01_unity_qalab.md 02_triage.md 03_vision.md 04_pipeline_ci.md
unity/
  com.miawworks.qalab/    UPM package: Runtime/ Editor/ Tests/
  QALabSandbox/           Unity project with seeded bugs (Sora creates it in Unity Hub in M1)
python/                   pyproject.toml, src/qalab/, tests/
scripts/                  PowerShell: unity_tests, build_sandbox, run_playtest, run_pipeline, benchmark
tools/cs-check/           .NET 8 test project: engine-free package C# + its EditMode tests, outside Unity (M1)
ci/Jenkinsfile            example nightly pipeline (M7)
.github/workflows/        python-ci.yml
.claude/rules/            path-scoped rules for Python and Unity C#
.claude/skills/           /milestone /teach /explain /review-mine /mock /status
```

## Commands (PowerShell, from repo root; each appears in the milestone that creates it)

```
py -3.12 -m venv .venv ; .\.venv\Scripts\Activate.ps1 ; pip install -e ".\python[dev]"
ruff check python ; ruff format python ; pytest python -q
pytest python -m youwrite -rxX                       # open YOU WRITE tasks (x = open)
dotnet test tools\cs-check --filter "TestCategory!=YouWrite"
qalab validate samples\sample_run
qalab triage run samples\sample_run --provider fake --out out\sample_report
scripts\unity_tests.ps1 -Platform EditMode
scripts\build_sandbox.ps1 ; scripts\run_playtest.ps1 -Seed 42 -Duration 120
scripts\run_pipeline.ps1 -Seed 42 -Duration 120 -Open
```

## First session (on Sora's PC)

If `docs/ENVIRONMENT.md` doesn't exist yet:

1. Inspect the machine: OS, CPU, RAM, GPU (`nvidia-smi`), Python versions, Git, Unity editors (Unity Hub folder), Ollama and its models.
2. Find Sora's Unity games on disk (ask where they are if you can't find them).
3. Write `docs/ENVIRONMENT.md`.
4. Recommend the model setup that fits this PC (see spec 02 §9).
5. Then propose the M0 plan. If M0 was already built in the cloud, go through its PR's PC checklist instead.

## Cloud mode

You're in cloud mode when `CLAUDE_CODE_REMOTE_SESSION_ID` is set: a claude.ai/code session on an Ubuntu VM with no Unity, Ollama or Windows, started with `docs/PROMPTS.md` → C1. Sora isn't watching; he reviews the PR. These rules replace the ones above where they conflict.

- **Scope:** one milestone per session, on the session's own branch (the only one you can push). End commit subjects with `(M<N>)`. Push every few commits.
- **Don't wait for "go".** Post a ≤ 10-line plan, then build. Stop and ask only for a schema change, a dependency not listed in spec 02/03, or a spec error that blocks the milestone. Decide the rest and log it in `docs/DECISIONS.md`.
- **Skip "First session".** `docs/ENVIRONMENT.md` and the model choice happen on Sora's PC.
- **Python 3.12:** `python3 -m venv .venv && . .venv/bin/activate && pip install -e "./python[dev]"` once `pyproject.toml` exists.
- **Engine-free C#** (no `UnityEngine`/`UnityEditor`) is compiled and its EditMode tests run in `tools/cs-check/`: a .NET 8 test project that links the package files (`LangVersion 9`, NUnit 3, Newtonsoft.Json 13). If `dotnet` is missing, `apt-get install -y dotnet-sdk-8.0`; if that fails, say so in the PR.
- **Can't verify here:** Unity APIs, scenes, builds, PlayMode, Ollama, PowerShell runs. Write it to spec, keep it thin, never claim it works, and put it in the PC checklist. Parse-check `.ps1` files with `pwsh` if it installs.
- **YOU WRITE: don't stop.** Write the stub (signature, docstring, `raise NotImplementedError("YOU WRITE")`) and its tests marked `@pytest.mark.youwrite`; `python/tests/conftest.py` makes them xfail while the stub raises, so CI stays green and wrong answers still fail. Tests of code that calls the stub get the marker too. To check that code you may use a temporary implementation in the working tree: restore the stub before every commit and never show the solution. C#: the stub throws `NotImplementedException` and its tests get `[Category("YouWrite")]`.
- **If GitHub rejects a push that touches `.github/workflows/`,** move the file to `ci/github/` and add "copy it to `.github/workflows/`" to the PC checklist.
- **Draft PR to main.** Body: Summary (≤ 8 lines) · Spec coverage (section → files → tests) · Checks run here (real output) · PC checklist (exact commands, in order) · Open YOU WRITE tasks (file, function, test command) · Read in this order (5 files, one line each) · Deviations from spec. Never merge, tag or push to main.

## Definition of done (every milestone)

- Acceptance checks in PLAN.md pass; tests and lint pass.
- Docs are updated: README section, DECISIONS (choices), EVAL_RESULTS (numbers), CHANGELOG (releases).
- YOU WRITE tasks were done by Sora and logged; `/teach` is done and logged.
- The milestone is ticked in PLAN.md, the branch is merged (with Sora's OK), and tagged if it's a release.
