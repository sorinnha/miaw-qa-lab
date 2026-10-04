# QA Lab user guide

For QC testers and developers who record playtests, read the bug reports and file them. Setup for a
new game is in [GAME_INTEGRATION.md](GAME_INTEGRATION.md); the design is in [ARCHITECTURE.md](ARCHITECTURE.md).

## What works today

| Part | Status |
|---|---|
| Recording runs in Unity (logs, metrics, markers, `run.json`, `labels.json`) | Built (M1). Verified outside Unity; first Unity run is on the PC checklist |
| `qalab validate`, `qalab triage run`, reports (HTML, Markdown, Jira CSV) | Built (M2–M3) |
| AI-written reports with Gemini or Ollama, RAG over design docs and code | Built (M3) |
| ProjectScanner (missing scripts, broken references, materials) | Built (M7) |
| Game adapter contracts and template | Built (M7); the bot runner that calls them is M4 |
| Seeded bot, detectors, screenshots, one-command pipeline | Planned (M4) |
| Vision (missing textures, black screens, broken UI) | Planned (M6) |

Two learning tasks still gate the end-to-end triage run: until `normalize_message` exists,
`qalab triage run` stops with `NotImplementedError: YOU WRITE`.

## 1. Set up once

```powershell
py -3.12 -m venv .venv ; .\.venv\Scripts\Activate.ps1 ; pip install -e ".\python[dev]"
pip install -e ".\python[dev,gemini]"     # only if you'll use Gemini
qalab --help
```

On Linux/macOS: `python3 -m venv .venv && . .venv/bin/activate && pip install -e "./python[dev]"`.

## 2. Record a playtest

**In the editor:** **Tools → QA Lab → Create or Select Settings**, tick **Auto-start in Play Mode**, press
Play, play, stop. **Tools → QA Lab → Open Last Run Folder** opens the run.

**In a built player** (a development build gets script stack traces), add flags:

| Flag | Default | Meaning |
|---|---|---|
| `-qalab` | off | Record a run |
| `-qalabOut <dir>` | `<persistentDataPath>/qalab/runs` | Parent folder; each run gets its own `<run_id>` subfolder |
| `-qalabSeed <int>` | 0 | Bot seed: the same seed makes the same bot decisions |
| `-qalabDuration <s>` | 120 | End the run after this many seconds |
| `-qalabAdapter <name>` | `navmesh_explorer` | Bot adapter, or `manual` for a human player |
| `-qalabScene <name>` | active scene | Scene to load first |
| `-qalabShotEvery <s>` | 5 | Periodic screenshots, 0 = off (M4) |
| `-qalabMinLevel <lvl>` | `warning` | Lowest log level recorded (`info` gives more context) |
| `-qalabSeeds <list>` | `all` | Sandbox only: which seeded bugs are on, e.g. `SB01,SB06` |
| `-qalabBenchmark` | off | Also write `labels.json` (ground truth, sandbox only) |
| `-qalabQuitOnEnd` | off | Quit the game when the run ends |

Example: `QALabSandbox.exe -qalab -qalabOut runs -qalabSeed 42 -qalabDuration 120 -logFile runs\player.log`.

**A run folder** (`<UTC time>-s<seed>`, for example `20261005T103000Z-s42`) holds:
- `run.json`: build, scenes, seed, adapter, start and end. No `ended_at` means the game crashed or was killed, and triage flags a possible crash;
- `events.jsonl`: one JSON event per line (logs, metrics, markers; actions, detectors and screenshots from M4). Lines can be slightly out of order; tools sort by `seq`. At most about 0.5 s of events is lost in a crash;
- `labels.json`: benchmark runs only. It is ground truth for evaluation, and triage never reads it.

The formats are fixed by `schemas/*.json` and described in `docs/specs/00_contracts.md`.

## 3. Check a run

```powershell
qalab validate runs\20261005T103000Z-s42      # or a parent folder, or a glob: runs\*
```

It prints `ok` or every invalid line with the schema rule it breaks. Exit code 0 means valid, 2 means
invalid or missing.

## 4. Triage

```powershell
qalab triage run runs\* --provider none --out out\report
qalab triage run samples\sample_run --provider gemini --docs docs\sandbox_design.md --out out\sample_report
```

| Option | Use |
|---|---|
| `--provider` | `none`: template reports, no model. `ollama`: local model. `gemini`: hosted, needs `GEMINI_API_KEY` in `.env`, **sandbox data only**. `fake`: tests. Default from `qalab.toml`. |
| `--docs <file.md>` | Design docs for context; reports cite their sections (`D` ids) |
| `--repo <folder>` | Game source; reports quote the code named in stack traces |
| `--cluster` | How logs are grouped: `frame_tfidf` (default), `exact`, `frame_embed`, `tfidf_only` |
| `--max-reports <n>` | Write reports for the n highest-ranked clusters only (default 25, `qalab.toml`) |
| `--no-cache` | Ignore cached model answers (`.cache/llm.sqlite`) |

The command writes `bugs.json`, `report.html`, `report.md`, `bugs_jira.csv`, `clusters.json`,
`validation_report.json` and `triage_meta.json` (provider, model, timings, token counts, cache hits;
prompts are never stored).

Exit codes: **0** done with no P1 bug, **3** done with at least one P1 (CI fails the build on it),
**2** error (bad input, missing key, provider down).

`qalab triage clusters runs\*` prints the clusters and scores without writing reports; use it to check
grouping. `qalab report html out\report` re-renders `report.html` from `bugs.json`.

## 5. Read the report

`report.html` is a single offline file. The top row counts bugs per priority. The filter box searches id,
priority, kind, title and summary. **Needs review only** narrows the list to reports the tool is unsure
about.

Each bug shows:
- **Priority (P1–P4)**: computed, never chosen by the model. P1 at score ≥ 15 or any blocker, P2 ≥ 8, P3 ≥ 3, P4 below 3 (tunable in `qalab.toml`):

  ```
  score = weight × (1 + log2(count)) × (1 + 0.5 × (runs_affected − 1)) × (1.25 if the run crashed)
  weight: exception 5, assert 4, error 3, warning 1; detector blocker 6 … trivial 1
  ```

  The score breakdown is shown with each bug.
- **Severity (S1–S4)**: from the model, or template rules. A gap of more than one level from the priority is flagged.
- **Steps to reproduce**: each step is marked:
  - `bot_log`: cites real bot actions (`A` ids);
  - `inferred`: the model's guess;
  - `template`: built from the action list.
- **Evidence**: the `E` ids are real events from the runs (message, stack, time, scene). Every claim must cite one.
- **Needs review** and its reasons. Typical reasons:
  - unknown evidence or doc ids were dropped;
  - a step claimed a bot action that doesn't exist;
  - the severity is far from the priority;
  - the confidence was low;
  - the model's draft failed validation after its retries, so the template report was used.

Treat `needs_review` reports as drafts. The rest still deserve a quick read before filing.

## 6. File bugs

- **Jira:** import `bugs_jira.csv` (Summary, Description, Issue Type = Bug, Priority P1→Highest … P4→Low,
  Labels = `qalab;<kind>`, Component). Map the columns once in Jira's CSV importer, then save the mapping.
- **Chat or wiki:** paste from `report.md`.
- Attach the run folder (or at least `run.json` + `events.jsonl`) to the ticket: it is the repro record.

## 7. Scan a project

**Tools → QA Lab → Scan Project** checks the enabled Build Settings scenes and every prefab under
`Assets/`, writes `Logs/qalab/scan.json` and prints a summary. From a terminal:

```powershell
scripts\scan_project.ps1                                          # the sandbox
scripts\scan_project.ps1 -ProjectPath D:\Games\MyGame -Out out\mygame_scan.json
```

| Rule | Severity | Meaning | Usual fix |
|---|---|---|---|
| `missing_script` | error | A component's script is gone (deleted, renamed, compile error) | Restore the script or remove the component |
| `broken_reference` | error | A field was set but its target was deleted ("Missing" in the Inspector) | Re-assign it |
| `null_material` | error | A renderer material slot is empty or its material was deleted (magenta) | Assign a material |
| `error_shader` | error | A material's shader is missing or failed to compile (magenta) | Fix or replace the shader |
| `missing_scene` | error | Build Settings lists a scene file that doesn't exist | Remove it from Build Settings |
| `unassigned_reference` | info | A script field was never set ("None"); often intended | Check it's optional |

Unassigned fields are reported for your own scripts only, because built-in components have many
optional slots.

`scan.json` contains:
- `tool`, `format_version` (1), `project`, `unity`, `scanned_at`;
- `scanned` (scenes, prefabs) and `summary` (errors, info);
- `findings[]`, errors first, each with `severity`, `rule`, `asset`, `object` (hierarchy path),
  `component`, `property` and `detail`.

Exit code 0 means no errors, 1 means errors found, 2 means the scan itself failed.

## 8. Benchmark mode (sandbox)

The sandbox has seeded bugs SB01–SB16 with known causes (`docs/specs/01_unity_qalab.md`). With
`-qalabBenchmark`, each triggered seed is recorded in `labels.json`, so evaluation can measure clustering
and detection against ground truth (M5). F1 in Play Mode opens a menu to trigger any seed by hand. Seed
ids never appear in log text, and triage never opens `labels.json`; a test enforces this.

## 9. CI

- **GitHub Actions** (`.github/workflows/python-ci.yml`), on every push:
  - lint and tests on Windows and Ubuntu;
  - a smoke triage of the sample run with the offline fake provider;
  - `tools/cs-check`, which compiles the engine-free package C# and runs its tests outside Unity.

  It needs no secrets and never calls a real model.
- **Jenkins:** `ci/Jenkinsfile` is an **example** nightly for a Windows agent with Unity:
  - Unity tests, build, parallel seeded playtests, triage, published `report.html`;
  - the build fails when triage finds a P1.

  It hasn't been run on a Jenkins server yet.

## Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `NotImplementedError: YOU WRITE` | A learning task is still a stub (`pytest python -m youwrite -rxX` lists them). |
| `GEMINI_API_KEY is not set` (exit 2) | Put the key in `.env` at the repo root, or use `--provider none`. |
| `qalab validate` reports bad lines | The run was written by an older package version, or the file was edited by hand: the message names the field. |
| No run folder after Play | Auto-start is off; or the editor runs tests (auto-start is skipped there). |
| A bug you expected is missing | Its level is below `-qalabMinLevel`, or it was grouped with another cluster: check `qalab triage clusters`. |
| Two different bugs in one report | Try `--cluster exact`; if they share message and top frames, they are one signature by design. |
