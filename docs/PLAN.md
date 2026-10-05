# Plan: Miaw QA Lab

**Goal:** a working, measured, explainable QA automation toolkit for Unity that shows every skill the Junior R&D Engineer (Gen AI/ML) role asks for, plus the confidence to talk about it in an interview.

**Pace:** about 2–3 hours a day. Doing more hours compresses the weeks; doing fewer, cut scope with the rules at the bottom, not quality.

## Timeline

| Week | Dates (2026) | Milestones | You'll have |
|---|---|---|---|
| 1 | Oct 3–9 | M0, M1 | Unity writes valid run folders; CI green |
| 2 | Oct 10–16 | M2, M3 | **v0.1.0**: AI bug reports from game logs. Send to recruiters |
| 3 | Oct 17–23 | M4 | **v0.2.0**: one command runs a bot playtest and produces a report |
| 4 | Oct 24–30 | M5, M6 | **v0.3–0.4**: measured results for triage and vision |
| 5 | Oct 31–Nov 8 | M7, M8 | **v1.0.0**: real game, demo video, interview-ready |

**Building in the cloud?** One cloud session per milestone builds the code (`docs/PROMPTS.md` → Cloud sessions). Unity, Ollama and PowerShell steps still run on your PC, and YOU WRITE + `/teach` still close every milestone.

**Every day:** 1.5–2 h milestone work with Claude Code, 30 min Python practice (2 easy string/array problems typed in a plain text box with no IDE, the format of the reported intern test), 5 min LEARNING.md.

---

## M0: Setup and contracts (Days 1–2)

**Build**
- `docs/ENVIRONMENT.md` from a machine check; model choice recorded.
- `python/pyproject.toml`, the `qalab` package skeleton, `qalab --help`, `qalab validate`.
- pydantic models for every schema, and contract tests (`schemas/examples`, `samples/sample_run`).
- ruff + pytest, GitHub Actions (spec 04) green, README badge.
- `.gitignore`, `.gitattributes` and `.env.example` are already here; check them.

**YOU WRITE:** `scripts/hello_events.py`, about 25 lines. Read `samples/sample_run/events.jsonl` and print a count per `kind` and per log `level`.

**Accept:**
- `pytest` passes locally and in CI on Windows + Ubuntu.
- `qalab validate samples/sample_run` passes, and it rejects every item in `events_invalid.json`.

**Covers:** Python scripting, Git, CI, contracts / SDLC.

## M1: Unity package core + sandbox log bugs (Days 3–5)

**Sora first:** in Unity Hub create the project `unity/QALabSandbox` (Universal 3D template, same Unity version as your games). Import TMP Essentials when asked.

**Build** (spec 01):
- package skeleton + asmdefs;
- CommandLine, RunContext (`run.json`), EventWriter (thread-safe JSONL), LogCapture, MetricsSampler;
- LabelRecorder;
- the `SandboxSceneBuilder` menu;
- seeds SB01–SB05, SB13 and SB14, with an F1 trigger menu;
- EditMode tests (command line parsing, JSON golden test against `schemas/examples`);
- `scripts/unity_tests.ps1`.

**YOU WRITE:** seeded bug **SB02** (`SeededInventory`) and its catalog entry.

**Accept:** 60 s of play with the seeds triggered writes a run folder that passes `qalab validate`, and EditMode tests pass.

**Covers:** C#, engine-level utilities, debugging, threading.

## M2: Triage core, no AI (Days 6–8)

**Build** (spec 02 §1–6, §11): load and validate, normalize, stack parsing, signatures, exact clustering and detector cells, ranking, template reports, `report.md`, `bugs.json`, `bugs_jira.csv`, and `qalab.toml`.

**YOU WRITE:** `normalize_message()`. Claude Code writes the tests from the spec 02 table first.

**Accept:**
- `samples/sample_run` gives 9 clusters under `exact`, SB13 ranks last, and `bugs.json` validates.
- Coverage ≥ 80% on `normalize`, `stack` and `signature`.

**Covers:** Python, regex, testing, debugging.

## M3: AI reports + RAG (Days 9–12) → release v0.1.0

**Build** (spec 02 §7–10):
- provider layer (Ollama, Fake, plus one hosted provider if Sora wants one), cache, structured output, retries, grounding checks;
- RAG over `docs/sandbox_design.md` and code context;
- `report.html`;
- README Quick start + a 30–60 s GIF.

**YOU WRITE:** `cosine_top_k()` with numpy.

**Accept:**
- A real local-model run on the sample gives valid reports where every evidence ID resolves and steps cite bot actions.
- No test touches the network; `report.html` works offline.

**Release:** tag v0.1.0, then send the follow-up message in `docs/OUTREACH.md`.

**Covers:** GenAI, RAG, prompt design, evaluation mindset, Python.

## M4: Bot, detectors, screenshots, one-command pipeline (Days 13–17) → v0.2.0

**Build** (spec 01 bot, detectors, screenshots, labels, build and run; spec 04 scripts):
- spike first: which screenshot method works on your PC;
- NavMesh explorer + UI crawler;
- stuck, fall, perf, exception-burst and (optional) tunneling detectors;
- seeds SB06–SB12, SB15 and SB16;
- JUnit results and exit codes;
- PlayMode tests;
- `build_sandbox.ps1`, `run_playtest.ps1`, `run_pipeline.ps1`.

**YOU WRITE:** `StuckCalculator` + `StuckDetector` in C#, with an EditMode test.

**Accept:** `scripts\run_pipeline.ps1 -Seed 42 -Duration 120 -Open` goes from nothing to `report.html` with no manual steps, including detector bugs with screenshots.

**Covers:** automation tests, engine tooling, C#, CI-ready output.

## M5: Triage evaluation (Days 18–20) → v0.3.0

**Build:**
- `scripts/benchmark.ps1` (20 seeds × 120 s);
- `qalab eval triage` metrics: pairwise precision/recall/F1 for clustering, cluster-count error, retrieval hit@3, field completeness, grounding rate, repro-step match rate, severity agreement, latency, tokens.

**Experiments:**
- **E1:** clustering variants (`exact`, `frame_tfidf`, `frame_embed`, `tfidf_only`).
- **E2:** two models (a small local one vs a larger or hosted one).
- **E3:** RAG on vs off.

Write the hypotheses first. Record tables, 2 charts, conclusions and limitations in `docs/EVAL_RESULTS.md`.

**YOU WRITE:** `pairwise_prf()`.

**Accept:** `qalab eval triage benchmarks\seeded_v1` reproduces every number in EVAL_RESULTS.md.

**Covers:** ML evaluation, metrics, R&D method (hypothesis → experiment → decision).

## M6: Vision (Days 21–25) → v0.4.0

**Build** (spec 03):
- dataset builder (split by run);
- heuristics;
- VLM classifier;
- optional ML baseline;
- hybrid policy;
- `qalab eval vision` with hypotheses H1–H4;
- visual bugs shown in the report with screenshots.

**YOU WRITE:** `magenta_ratio()`.

**Accept:** spec 03 acceptance criteria.

**Covers:** VLMs, classic CV/ML, cost/latency trade-offs.

## M7: Real game + polish (Days 26–31) → v1.0.0

**Build:**
- install the package in **Crimson Tactics** (default; switch to Dead District if that integrates more easily) via git URL;
- a game adapter that drives the game's own commands;
- 3 runs triaged; ProjectScanner (spec 01) run on the game;
- an honest write-up of what it found in `docs/REAL_GAME.md`;
- `docs/USER_GUIDE.md` (for a QC tester);
- `ci/Jenkinsfile`;
- final README;
- a 2-minute demo video;
- v1.0.0 release.

**YOU WRITE:** the game adapter, with Claude Code pairing on it.

**Final README outline:**
1. Problem
2. Demo GIF
3. What it does
4. Architecture diagram
5. Quick start
6. Results (from EVAL_RESULTS)
7. Design decisions
8. Limitations
9. Roadmap
10. Data handling
11. How it was built (honest note on AI pair programming)

**GitHub cleanup** (outside this repo):
- pin miaw-qa-lab plus your 2 best game repos;
- archive the forks and "first time using Unity" repos;
- add a README with a GIF to each pinned game repo.

**Covers:** integration into a real testing environment, documentation, CI/CD.

## M8: Interview readiness (Days 28–35, overlaps M7)

- Go through `docs/INTERVIEW_PREP.md` until you can explain every module out loud without notes.
- Run `/mock` on each topic, then `/mock full` twice. Fix the gaps it finds.
- Second outreach round with the v1.0.0 link (`docs/OUTREACH.md`).
- Stretch goals:
  - replay mode (re-run a recorded action log to confirm a bug reproduces);
  - Jenkins in Docker for a real nightly run;
  - GameCI for Unity tests in GitHub Actions.

---

## If you're short on time

1. **Minimum viable:** M0–M3 + E1 from M5. That's a credible GenAI QA tool with a measured result.
2. Next most valuable: M4 (automation and engine tooling), then M7 (real game), then M6.
3. Never skip: tests, YOU WRITE tasks, `/teach`, honest numbers.

## Checklist

- [ ] M0 · [ ] M1 · [ ] M2 · [ ] M3 (v0.1.0) · [ ] M4 (v0.2.0) · [ ] M5 (v0.3.0) · [ ] M6 (v0.4.0) · [ ] M7 (v1.0.0) · [ ] M8

## Change log

| Date | Change | Why |
|---|---|---|
| 2026-10-03 | Plan created | Kick-off |
| 2026-10-03 | Cloud mode: milestones can be built in cloud sessions; `tools/cs-check` tests engine-free C# (D-006) | Faster build while keeping YOU WRITE and honest checks |
| 2026-10-03 | M1: the SB02 YOU WRITE catalog entry is C# (`SandboxSeedCatalog.SB02`), not a ScriptableObject entry (D-017) | Reviewable, schema-checked in cs-check |
| 2026-10-04 | M7 code built before M4–M6 in a polish session: ProjectScanner, the bot contracts from spec 01 (M4 interfaces) with a game adapter template, `ci/Jenkinsfile`, `USER_GUIDE.md`, `GAME_INTEGRATION.md`, final README outline (D-024) | Polish request; the template needs the contracts; the bot runner stays in M4 |
| 2026-10-04 | Versions stay 0.1.0 until the first tag; `CHANGELOG.md` lists the work as Unreleased (D-024) | No release has been cut yet |
| 2026-10-04 | M7 YOU WRITE made concrete: the adapter template's decision rule `TurnPolicy.Decide` (stub + YouWrite tests in `tools/cs-check`), then `IGameCommands` for the real game (D-024) | The template must not contain the learning task's solution |
| 2026-10-05 | M4 built in a cloud session; the stuck detector is the YOU WRITE task (stubs + YouWrite tests in cs-check, skipped at runtime until written) (D-025) | Plug-and-play request: everything but the Unity/PC steps |
| 2026-10-05 | NavMesh explorer biased towards less-visited cells; sandbox gets a south-east corridor over T_17 and a north-west room behind the SB07 gap; SB07's gap read as diameters (0.76 m) (D-025, D-026) | Plain random targets rarely reach the edge tile or a side room; the spec's "radius" wording can't produce a NavMesh-only passage |
| 2026-10-05 | `run_pipeline.ps1` also runs a 30 s UI-crawler playtest of Sandbox_Menu (`-MenuCrawl 0` skips it); `run_playtest.ps1` adds exit codes 3 (no run folder) and 4 (timeout) (D-027) | SB15 is only reachable from the menu; scripts must say why there is no run |
| 2026-10-05 | Sample run SB06 moved to T_17's real position (35, 1) (D-022, D-026) | Ground truth follows the builder |
| 2026-10-05 | M5 built in a cloud session: `qalab eval triage` + `benchmark.ps1`; `pairwise_prf` is the YOU WRITE task (D-028) | Plug-and-play request: everything but the benchmark recording on the PC |
| 2026-10-05 | E2/E3 report table gains "component correct", "severity ±1" and the same columns for every setting; "expected filled correctly" stays a manual 10-report check; `--design-doc` scores RAG-off runs (D-028) | One table format, printed by the tool; "expected is correct" needs human judgment |
