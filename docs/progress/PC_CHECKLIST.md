# What Sora does on the PC: every milestone

The one hand-off note. Cloud sessions build and test code without Unity, Windows or a real model. Everything
that needs those, plus the learning tasks, `/teach` and releases, is listed here in order. Tick items as
you go. When a milestone's list is done, tick it in `docs/PLAN.md` → Checklist.

Run commands in PowerShell from the repo root with the virtual environment active
(`.\.venv\Scripts\Activate.ps1`).

## State of the code (for the next session)

- **On `main`:**
  - M0: contracts, CLI, CI;
  - M1: Unity package recording, sandbox log seeds, scripts;
  - M2–M3: triage, LLM layer with fake/Ollama/Gemini, RAG, reports;
  - M7 code: ProjectScanner, bot contracts, game adapter template, `ci/Jenkinsfile`, USER_GUIDE, GAME_INTEGRATION, final README outline.
- **On the M4 branch (`m4-bot-pipeline`, draft PR):** bot runner, NavMesh explorer (coverage-biased), UI crawler, detector hub and the fall / perf / exception-burst / tunneling detectors, screenshots with visual labels, results.xml and exit codes, BuildRunner, the QA Lab window, seeds SB06–SB12, SB15, SB16, `build_sandbox.ps1` / `run_playtest.ps1` / `run_pipeline.ps1`. The stuck detector is your YOU WRITE task; until then it is skipped at runtime.
- **On the M5 branch (`m5-eval`, draft PR stacked on M4):** `qalab eval triage` (E1 clustering P/R/F1, E2/E3 report quality, D-028) and `scripts\benchmark.ps1`. `pairwise_prf` is your YOU WRITE task.
- **Not built (needs a cloud session first):** M6 vision.
- **Never run in Unity yet:** everything under `unity/` except the engine-free files, which `tools/cs-check` compiles and tests. The first Unity session will likely surface compile errors. Paste them to Claude.
- **Decisions to know:** D-021 (Gemini is the configured provider, sandbox data only), D-023 (clean run end, disjoint seed rules), D-024 (M7 code before M4, versions stay 0.1.0 until the first tag), D-025–D-027 (M4 runtime, sandbox seeds, scripts).

### Open learning tasks (YOU WRITE)

| Milestone | File | Function | Check it with |
|---|---|---|---|
| M0 | `scripts/hello_events.py` | `count_events`, `main` | `pytest python/tests/test_hello_events.py -rxX`, then `python scripts/hello_events.py` matches the docstring |
| M1 | `unity/QALabSandbox/Assets/Sandbox/Scripts/SeededBugs/SeededInventory.cs` | `GetSlot` (SB02) | `scripts\unity_tests.ps1 -Platform EditMode -IncludeYouWrite` |
| M1 | `unity/QALabSandbox/Assets/Sandbox/Scripts/SandboxSeedCatalog.cs` | `SB02()` | `dotnet test tools\cs-check -c Release --filter TestCategory=YouWrite` |
| M2 | `python/src/qalab/triage/normalize.py` | `normalize_message` | `pytest python/tests/triage/test_normalize.py -rxX` |
| M3 | `python/src/qalab/rag/retrieve.py` | `cosine_top_k` | `pytest python/tests/rag/test_retrieve.py -rxX` |
| M4 | `unity/com.miawworks.qalab/Runtime/Detectors/StuckCalculator.cs` and `StuckDetector.cs` | `StuckCalculator.Add`, `Reset`, `SampleCount`; `StuckDetector.Tick` | `dotnet test tools\cs-check -c Release --filter "FullyQualifiedName~StuckCalculatorTests"` (11 tests), then in Unity `scripts\unity_tests.ps1 -Platform EditMode -IncludeYouWrite` |
| M5 | `python/src/qalab/eval/metrics.py` | `pairwise_prf` | `pytest python/tests/eval -m youwrite -rxX` |
| M6 | `python/src/qalab/vision/heuristics.py` (to be created) | `magenta_ratio` | after the M6 cloud session |
| M7 | `unity/com.miawworks.qalab/Samples~/GameAdapterTemplate/TurnPolicy.cs`, then your game | `TurnPolicy.Decide`, then `IGameCommands` for the real game | `dotnet test tools\cs-check -c Release --filter "FullyQualifiedName~TurnPolicy"`; `docs/GAME_INTEGRATION.md` §4 |

`pytest python -m youwrite -rxX` lists the open Python ones (x = still open, X = passing, awaiting
`/review-mine`). After each task: `/review-mine`, then `/teach`, then a line in `docs/LEARNING.md`.

## Every session

- [ ] `git checkout main ; git pull`, then a new branch `m<N>-<slug>` for your own changes.
- [ ] `pytest python -q` passes, with xfails only for open learning tasks.
- [ ] Optional: `dotnet test tools\cs-check -c Release --filter "TestCategory!=YouWrite"` (needs the .NET 8 SDK).

## M0: Setup and contracts

- [ ] First Claude Code session on the PC (CLAUDE.md "First session"): it checks the machine (OS, CPU, RAM, GPU, Python, Git, Unity editors, Ollama), finds your games and writes `docs/ENVIRONMENT.md`. Record the model choice there (Gemini, D-021).
- [ ] `py -3.12 -m venv .venv ; .\.venv\Scripts\Activate.ps1 ; pip install -e ".\python[dev]"`
- [ ] `qalab validate samples\sample_run` prints `ok`. `pytest python -q` passes.
- [ ] GitHub → Actions: `python-ci` is green on the latest `main`, and the README badge shows it.
- [ ] YOU WRITE `hello_events.py` (table above), then `/review-mine`, `/teach`, LEARNING.md.

## M1: Unity package and sandbox

- [ ] **Unity Hub → New project → Universal 3D**, your games' Unity version, name `QALabSandbox_new`, location `<repo>\unity`. Open it once, close it.
- [ ] Merge it into the sandbox folder:
  ```powershell
  robocopy unity\QALabSandbox_new unity\QALabSandbox /E /XD Library Temp Logs obj UserSettings
  Remove-Item -Recurse -Force unity\QALabSandbox_new
  python scripts\link_sandbox_package.py
  ```
- [ ] Open `unity\QALabSandbox` in Unity Hub (Add → from disk).
  - **Edit → Project Settings → Player → Product Name: `QALabSandbox`** (the copy still says `_new`).
  - Install **AI Navigation** (Package Manager); import TMP Essentials if asked.
  - Fix Console errors with Claude: this code has never been compiled by Unity.
- [ ] **Tools → QA Lab → Rebuild Sandbox Scenes**. Expect `[QALab] rebuilt ...`.
- [ ] `scripts\unity_tests.ps1 -Platform EditMode` and `-Platform PlayMode`. Expect `failed=0`, and the script returns right after Unity exits.
- [ ] **Acceptance:**
  - Open `Sandbox_Level01`, press Play, **F1 → Trigger all**, walk the gravel path, press Tab and E near Door_02. Play 60 s, then stop.
  - `qalab validate runs\<newest>` prints `ok`.
  - In `events.jsonl`, `run_start` is seq 0 and `run_end` is last with the highest seq. `labels.json` lists SB03 and SB04 separately.
- [ ] Commit the generated files: `unity/QALabSandbox/**` (ProjectSettings, Packages, Assets with `.meta`, scenes, materials, the NavMesh asset) and every new `.meta` under `unity/com.miawworks.qalab/`.
- [ ] YOU WRITE `GetSlot` and `SB02()`, then `/review-mine`, `/teach`, LEARNING.md.

## M2: Triage core

- [ ] YOU WRITE `normalize_message`; `/review-mine` removes the `youwrite` markers once it passes.
- [ ] `qalab triage clusters samples\sample_run --cluster exact` shows 9 clusters. Check the ranking against D-012.
- [ ] `qalab triage run samples\sample_run --provider fake --out out\sample_report` exits 3 or 0 and writes all seven files. Open `report.html`.
- [ ] `pytest python -q --cov=qalab.triage --cov-report=term-missing` shows coverage ≥ 80% on normalize, stack and signature. Record the numbers in PLAN.md.
- [ ] `/teach`, LEARNING.md.

## M3: AI reports and RAG → v0.1.0

- [ ] YOU WRITE `cosine_top_k`, then `/review-mine`.
- [ ] Put `GEMINI_API_KEY=...` in `.env` yourself (copy `.env.example`). Never paste the key into chat. Then `pip install -e ".\python[dev,gemini]"`.
- [ ] `qalab triage run samples\sample_run --provider gemini --docs docs\sandbox_design.md --out out\gemini_report`. Check that:
  - every evidence id resolves;
  - steps marked `bot_log` cite real actions;
  - `needs_review` reasons make sense;
  - `triage_meta.json` has tokens and latency.

  If a model name is rejected, set `[llm] model` or `embed_model` in `qalab.toml`.
- [ ] Record a 30–60 s GIF of `report.html` and replace the README §2 placeholder.
- [ ] Ask Claude to prepare v0.1.0: CHANGELOG `[Unreleased]` → `[0.1.0]` (the four version constants are already 0.1.0), tag, and a GitHub Release with `report.html`. Claude asks before tagging. Then send the follow-up in `docs/OUTREACH.md`.

## M4: Bot, detectors, screenshots, pipeline → v0.2.0

Built in the cloud (D-025–D-027); none of it has run in Unity yet. Steps in order:

- [ ] Check out `m4-bot-pipeline` (or `main` once the PR is merged). Open `unity\QALabSandbox` and let it compile. Paste any Console errors to Claude (new APIs: NavMesh, ScreenCapture, ExecuteEvents, Physics.Linecast, `NamedBuildTarget`).
- [ ] **Tools → QA Lab → Rebuild Sandbox Scenes.** The Console must not show `Humanoid agent radius is ...`. Check:
  - Window → AI → Navigation → Agents: Humanoid radius **0.3**;
  - Scene view with the NavMesh shown: a thin blue strip passes through the north-west doorway gap (x ≈ 5.8, z = 30);
  - T_17 (x 34–36, z 0–2, inside the south-east corridor) is drawn and is blue on the NavMesh.
- [ ] **Screenshot spike** (spec 01, M4 step 1). Record the result as a new decision in DECISIONS.md:
  - Play Mode with the settings asset: press **F12**, check `runs\<id>\shots\` has a PNG with the HUD (`screen_capture`);
  - after `build_sandbox.ps1`: a `run_playtest.ps1` run's shots include the HUD;
  - once with `-batchmode` added to the player arguments (edit `run_playtest.ps1` locally, don't commit): shots are `camera_render`, without the HUD.
- [ ] **F1 menu, one seed at a time** (Play Mode, settings asset on, adapter `manual`). After each, stop and look at `events.jsonl` / `labels.json`:
  - SB06 "Stand on tile T_17": the player falls and respawns at the start; a `fell_out_of_world` event, critical, near (35, 1);
  - SB07 "Go to the narrow gap", then walk north (W): you can't pass. A `stuck` event only after your YOU WRITE task, and only when the bot is driving;
  - SB08 "GC burst now": a `perf_spike` event. If not, raise GcZone's `megabytes` (Inspector) until it does and note the value in D-026;
  - SB09 "Look at Crate_07": the crate is magenta;
  - SB10 "Black out the camera now": 2 s of black with the HUD still drawn;
  - SB11 "+10000 points": the score text runs out of its dark box;
  - SB12/SB16 "Go to the ballistics range": the ammo icon is a white box, projectiles fly through the thin wall, and `tunneling` events appear;
  - SB15: open `Sandbox_Menu`, Play, Settings → Apply: an InvalidOperationException in the Console.
- [ ] Check the bot by hand: settings asset adapter `navmesh_explorer`, press Play: the player walks by itself, `action` events (`move_to`, `interact`) appear.
- [ ] `scripts\unity_tests.ps1 -Platform EditMode` and `-Platform PlayMode`, both `failed=0`. New: `BotAndDetectorTests`, `BuiltInAdapterTests`, `SeededLevelTests` (the projectile tests check the SB16 premise on your PC's physics), `HudScoreTests`.
- [ ] YOU WRITE `StuckCalculator` + `StuckDetector` (table above) until the 11 cs-check tests pass, then `/review-mine` (it drops `[Category("YouWrite")]`), `/teach`, LEARNING.md.
- [ ] Close the editor. `scripts\build_sandbox.ps1` prints `built ...\Builds\Sandbox\QALabSandbox.exe`.
- [ ] `scripts\run_playtest.ps1 -Seed 42 -Duration 120` opens a 1280×720 window and prints `exit N, ... actions, ... detector events, ... screenshots` and the run folder. Expect ≥ 30 actions (if fewer, note the count and the seed in the PR, then try `-Seed 1` and `-Seed 7`: a bot that keeps giving up shows `give_up` actions in `events.jsonl`). `qalab validate runs\<id>` prints `ok`. `results.xml` lists the detectors; `labels.json` lists screenshots with labels.
- [ ] **Acceptance:** `scripts\run_pipeline.ps1 -Seed 42 -Duration 120 -Open` goes from nothing to `report.html` with detector bugs and their screenshots. Triage needs your M2 and M3 YOU WRITE functions (`normalize_message`, `cosine_top_k`). The bot is random: if seed 42 misses SB06 or SB07, note which seeds find them; M5's benchmark measures the rates.
- [ ] Commit what Unity generated: both scenes, `NavMesh-Sandbox_Level01.asset`, the new materials, `ProjectSettings/NavMeshAreas.asset`, and every new `.meta` file (package and sandbox).
- [ ] Release v0.2.0 (ask first).

## M5: Triage evaluation → v0.3.0

Built in the cloud (D-028); it has only run on copies of the sample run. Needs M4 working first (a
built sandbox player and your M2/M3 YOU WRITE functions). Steps in order:

- [ ] YOU WRITE `pairwise_prf` (table above) until `pytest python/tests/eval -m youwrite -rxX` shows only `X`, then `/review-mine`, `/teach`, LEARNING.md.
- [ ] Write the E2 and E3 hypotheses in `docs/EVAL_RESULTS.md` **before** running anything (E1's is there; adjust it if you disagree). Commit them first, so the git history shows they came before the numbers.
- [ ] `scripts\benchmark.ps1 -Seeds (1..20) -Duration 120` (about an hour: 20 × (120 s + 30 s menu crawl + loading)). It ends with `benchmark: 40/40 playtests recorded`. If some failed, `manifest.json` lists their exit codes, and timed-out partial runs are in `benchmarks\seeded_v1\_failed\`. Re-run only those seeds with `-Seeds 3,7`: their manifest entries are replaced and the others kept.
- [ ] Sanity check: `qalab eval triage benchmarks\seeded_v1 --variants exact --provider none --label check`. The first line lists the seeds that fired (labels.json) and the seeds with matching events (what triage can find). Expect most log and detector seeds in the second list (SB01–SB08, SB13–SB16; SB07 only once your stuck detector works, though it can fire without it; SB09–SB12 are visual and only count in M6). Bugs the bot never reached are absent, which is a finding for the README limitations, not an error. `ambiguous_events` in the JSON must be 0.
- [ ] E1, E2 and E3: the exact commands are in `docs/EVAL_RESULTS.md`. Choose E2's two models first (no Ollama here, D-021). Paste each table from `eval\triage_<label>.md` and copy the charts to `docs/img/`.
- [ ] E3 manual column: read 10 reports per setting and count the correct "expected" fields.
- [ ] Write Conclusions and Limitations in EVAL_RESULTS, then README §6, using only those numbers. Every number must have its command.
- [ ] Commit `docs/` only. `benchmarks/` and `eval/` stay out of git (too big; the manifest and the commands make them reproducible).
- [ ] Release v0.3.0 (ask first).

## M6: Vision → v0.4.0

- [ ] Cloud session first (M6).
- [ ] Build the dataset from benchmark runs (split by run, not by frame).
- [ ] YOU WRITE `magenta_ratio`.
- [ ] `qalab eval vision` with hypotheses H1–H4 written first. Fill EVAL_RESULTS and README §6.
- [ ] Release v0.4.0 (ask first).

## M7: Real game → v1.0.0 (scanner, adapter contracts, template and Jenkinsfile are built)

- [ ] In the sandbox:
  - **Tools → QA Lab → Scan Project** prints `[QALab] scan: ...` and opens `Logs/qalab/scan.json`;
  - `scripts\scan_project.ps1` prints its summary line, exit 0 or 1 (2 means it didn't run: read the `.log`);
  - the EditMode tests include `ProjectScannerTests`, `ScanReportTests` and `BotFrameworkTests`.
- [ ] Check the missing-script rule by hand: on a throwaway prefab, add a small script component, delete the script, scan, and expect `missing_script`. Then delete the prefab.
- [ ] Install the package in Crimson Tactics by git URL (`docs/GAME_INTEGRATION.md` §1–2). Record a manual run and run `qalab validate` on it.
- [ ] YOU WRITE `TurnPolicy.Decide` in the template until its cs-check tests pass, then `/review-mine` (it drops the `YouWrite` category).
- [ ] Import the **Game adapter template** sample into the game and implement `IGameCommands`, pairing with Claude (§4). Run it with `-qalabAdapter my_game` (the M4 bot runner calls it).
- [ ] Triage 3 runs with `--provider none` (or `ollama`), **never Gemini**, plus `--repo <game>\Assets`.
- [ ] `scripts\scan_project.ps1 -ProjectPath <game> -Out out\game_scan.json`.
- [ ] Write `docs/REAL_GAME.md` honestly: what was found, false positives, scanner results, measured numbers only.
- [ ] Optional: run `ci/Jenkinsfile` on Jenkins in Docker. Only with a screenshot of a green run, drop the "example" label in README, USER_GUIDE and the file header.
- [ ] Final README: §2 GIF, §6 results, the 2-minute demo video link.
- [ ] Release v1.0.0 (ask first). Set the version at release time in `python/pyproject.toml`, `python/src/qalab/__init__.py`, `unity/com.miawworks.qalab/package.json` and `QALab.Version` (`Runtime/Core/QALab.cs`), plus the CHANGELOG (D-024; `pytest` checks they agree).
- [ ] GitHub cleanup (PLAN.md M7): pin repos, archive forks, add READMEs with GIFs.

## M8: Interview readiness

- [ ] Explain every module in `docs/INTERVIEW_PREP.md` → "What's actually built" out loud, without notes.
- [ ] `/mock` on each topic, then `/mock full` twice. Fix the gaps it finds.
- [ ] Second outreach round with the v1.0.0 link (`docs/OUTREACH.md`).
