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
| Game adapter contracts and template | Built (M7) |
| Seeded bot, detectors, screenshots, results.xml, one-command pipeline | Built (M4). Not yet run in Unity: first run is on the PC checklist. The stuck detector is a learning task, skipped until written |
| Benchmark and `qalab eval triage` (clustering and report scores against seeded ground truth) | Built (M5). Needs a recorded benchmark; `pairwise_prf` is a learning task, so scoring stops with `NotImplementedError` until it is written |
| Vision: `qalab vision analyze` (missing textures, black screens, white placeholder boxes, UI overflow), visual bugs in reports, `qalab eval vision` | Built (M6). Needs real screenshots to tune; `magenta_ratio` is a learning task, so `--method heuristic` and `hybrid` stop with `NotImplementedError` until it is written (`--method vlm` works) |

Two learning tasks still gate the end-to-end triage run. Until `normalize_message` exists, every
`qalab triage run` stops with `NotImplementedError: YOU WRITE`. Until `cosine_top_k` exists, so does a
run with `--docs` and an embedding provider (fake, Ollama, Gemini); `--provider none` retrieves with
TF-IDF instead.

## 1. Set up once

```powershell
py -3.12 -m venv .venv ; .\.venv\Scripts\Activate.ps1 ; pip install -e ".\python[dev]"
pip install -e ".\python[dev,gemini]"     # only if you'll use Gemini
qalab --help
```

On Linux/macOS: `python3 -m venv .venv && . .venv/bin/activate && pip install -e "./python[dev]"`.

## 2. Record a playtest

**In the editor:** **Tools → QA Lab → Window**. Create the settings if asked, pick the adapter
(`manual` to play yourself, `navmesh_explorer` to let the bot play) and press **Play with QA Lab**. Stop
Play Mode (or wait for the duration) to end the run. The window shows the last run's results and opens
its folder and report. **F12** takes a screenshot (editor and development builds).

**A bot playtest of a built player** (the sandbox):

```powershell
scripts\build_sandbox.ps1                                   # Builds\Sandbox\QALabSandbox.exe (close the editor first)
scripts\run_playtest.ps1 -Seed 42 -Duration 120 -Benchmark  # prints the run folder
scripts\run_pipeline.ps1 -Seed 42 -Duration 120 -Open       # build if needed, playtest + menu crawl, triage, open report.html
```

`run_playtest.ps1` passes the player's exit code through (below), plus 3 when no run folder was
written and 4 when the player hung and was stopped. Other flags: `-Adapter ui_crawler -Scene Sandbox_Menu`,
`-Seeds SB01,SB06`, `-ShotEvery 2`, `-Out <dir>`.

**In a built player** (a development build gets script stack traces), add flags:

| Flag | Default | Meaning |
|---|---|---|
| `-qalab` | off | Record a run |
| `-qalabOut <dir>` | `<persistentDataPath>/qalab/runs` | Parent folder; each run gets its own `<run_id>` subfolder |
| `-qalabSeed <int>` | 0 | Seed for the bot's random choices (same seed, same random sequence; the action log is the repro record) |
| `-qalabDuration <s>` | 120 | End the run after this many seconds |
| `-qalabAdapter <name>` | `navmesh_explorer` | Bot adapter, or `manual` for a human player |
| `-qalabScene <name>` | active scene | Scene to load first |
| `-qalabShotEvery <s>` | 5 | Periodic screenshots, 0 = off |
| `-qalabMinLevel <lvl>` | `warning` | Lowest log level recorded (`info` gives more context) |
| `-qalabSeeds <list>` | `all` | Sandbox only: which seeded bugs are on, e.g. `SB01,SB06` |
| `-qalabBenchmark` | off | Also write `labels.json` (ground truth, sandbox only) |
| `-qalabQuitOnEnd` | off | Quit the game when the run ends |

Example: `QALabSandbox.exe -qalab -qalabOut runs -qalabSeed 42 -qalabDuration 120 -qalabQuitOnEnd -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile runs\player.log`.

**Bots** (`-qalabAdapter`):
- `navmesh_explorer`: walks to random reachable points on the baked NavMesh, preferring 4 m cells it
  hasn't visited, and tries to interact with what's in range (20% of steps). Needs a baked NavMesh and a
  registered player.
- `ui_crawler`: clicks one random active, interactable uGUI control per step; skips names containing
  Quit or Exit (the sandbox also skips Play, so a menu crawl stays in the menus).
- a game's own adapter (GAME_INTEGRATION.md §4), or `manual` for no bot.

**Detectors** watch every frame, bot or human:

| Detector | Fires when | Severity |
|---|---|---|
| `fell_out_of_world` | the player is below the kill plane (lowest floor − 5 m, or the game's setting); the player is respawned | critical |
| `stuck` | the bot asks the player to move but it moved < 0.5 m in 4 s | major |
| `perf_spike` | a frame takes > 50 ms (not counting QA Lab's own screenshot frames or scene loads) | minor |
| `exception_burst` | more than 20 exceptions within 1 s | major |
| `tunneling` | a fast projectile with QA Lab's `TunnelingDetector` passed through a collider without hitting it | major |

Each detector reports a given 4 m area at most once per 10 s, and every report comes with a screenshot.

**Exit codes** with `-qalabQuitOnEnd`: **0** no blocker or critical finding, **1** a blocker or critical
finding (e.g. the player fell out of the world), **2** QA Lab itself failed (see `results.xml`).

**A run folder** (`<UTC time>-s<seed>`, for example `20261005T103000Z-s42`) holds:
- `run.json`: build (with the commit for players built by `build_sandbox.ps1`), scenes, seed, adapter, start and end, exit code. No `ended_at` means the game crashed or was killed, and triage flags a possible crash;
- `events.jsonl`: one JSON event per line (logs, bot actions, detector findings, screenshots, metrics, markers). Lines can be slightly out of order; tools sort by `seq`. At most about 0.5 s of events is lost in a crash;
- `shots/000001.png`, ...: screenshots (periodic, on every detector event, F12), long side ≤ 1280 px;
- `results.xml`: JUnit summary for CI: one test case per detector (fails with the count), `no_exceptions`, and `no_internal_errors`;
- `player.log`: the player's own log (`run_playtest.ps1` moves it in);
- `labels.json`: benchmark runs only. It is ground truth for evaluation (seeded bugs, and which screenshots show which visual bug), and triage never reads it.

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

**Visual bugs.** Run vision before triage, and triage picks its findings up:

```powershell
qalab vision analyze runs\* --method heuristic          # free and offline; writes visual_findings.jsonl per run
qalab vision analyze runs\* --method hybrid --provider gemini   # heuristics + the model where they are blind
```

| `--method` | What it finds | Cost |
|---|---|---|
| `heuristic` | `missing_texture` (magenta pixels), `black_screen`, `placeholder_ui` (solid white boxes) | free, milliseconds per frame |
| `vlm` | all four labels, including `ui_overflow` (text out of its box) | one model call per frame |
| `hybrid` | heuristics first; the model only near UI actions/detector events and on every 5th other frame (`qalab.toml [vision]`) | a fraction of `vlm` |
| `ml` | the labels the baseline was trained on (`--ml-model`, from `qalab vision train-ml`) | free |

Each label with score ≥ 0.5 becomes a `visual:<label>` bug (`black_screen` major, others minor), with
the clearest screenshot attached. `events.jsonl` is never changed: delete `visual_findings.jsonl` to
triage without vision. The model only sees screenshots; use `heuristic` or a local model for
unreleased games.

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

Unassigned fields are reported for scripts outside Unity's own packages (`Packages/com.unity.*`: uGUI,
TextMeshPro, AI Navigation...), because their components and Unity's built-in ones have many optional
slots.

`scan.json` contains:
- `tool`, `format_version` (1), `project`, `unity`, `scanned_at`;
- `scanned` (scenes, prefabs) and `summary` (errors, info);
- `findings[]`, errors first, each with `severity`, `rule`, `asset`, `object` (hierarchy path),
  `component`, `property` and `detail`.

Exit code 0 means no errors, 1 means errors found, 2 means the scan didn't run or wrote no `scan.json`
(Unity not found, compile errors, project already open in another editor: see the `.log` next to it).
The menu item is greyed out in Play Mode.

## 8. Benchmark mode (sandbox)

The sandbox has seeded bugs SB01–SB16 with known causes (`docs/specs/01_unity_qalab.md`). With
`-qalabBenchmark`, each triggered seed is recorded in `labels.json`, so evaluation can measure clustering
and detection against ground truth (M5). Visual seeds (SB09–SB12) count only when a screenshot shows
them, and each screenshot lists its visual labels (M6). F1 in Play Mode opens a menu to trigger any seed
by hand or walk to it. Seed ids never appear in log text, and triage never opens `labels.json`; a test
enforces this.

**Score the tool on the sandbox** (M5):

```powershell
scripts\benchmark.ps1 -Seeds (1..20) -Duration 120          # one explorer run + one 30 s menu crawl per seed
qalab eval triage benchmarks\seeded_v1 --provider none      # E1: clustering P/R/F1 per variant
qalab eval triage benchmarks\seeded_v1 --variants none --reports --docs docs\sandbox_design.md --label rag-on
```

`benchmarks\seeded_v1\manifest.json` records the seeds, durations, commit, machine and every run's exit
code. Each `qalab eval triage` writes to `eval\`:
- `triage_<label>.json`: every number;
- `triage_<label>.md`: the tables for `docs/EVAL_RESULTS.md`;
- charts;
- `reports_<label>\`: the reports that were scored.

`--design-doc docs\sandbox_design.md` scores the component field when RAG is off (no `--docs`). The
metric definitions are in DECISIONS D-028.

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
| `run_playtest.ps1` exit 3 | The player wrote no run folder: read the `player-*.log` it names (QA Lab didn't start, or the player crashed on load). |
| The bot stands still | No baked NavMesh in the scene, or the game never called `QALab.RegisterPlayer`. The Console says which adapter started. |
| `detector 'stuck' is not implemented yet` | The stuck detector is a learning task (PLAN.md M4); the rest of the run is fine. |
| No `fell_out_of_world` although the player fell | The game respawned the player first: set `QALab.KillPlaneY` above the game's own respawn height. |
| A bug you expected is missing | Its level is below `-qalabMinLevel`, or it was grouped with another cluster: check `qalab triage clusters`. |
| Two different bugs in one report | Try `--cluster exact`; if they share message and top frames, they are one signature by design. |
