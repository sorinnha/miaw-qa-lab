# Changelog

All notable changes to Miaw QA Lab. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
versions follow [Semantic Versioning](https://semver.org/) and the release plan in `docs/specs/04_pipeline_ci.md`.
The Unity package has its own changelog in `unity/com.miawworks.qalab/CHANGELOG.md`.

Nothing is released yet. The first tag, v0.1.0, comes after M3's acceptance run on Sora's PC
(D-024). Until then everything below is unreleased, and the Python package and the Unity package both
stay at version 0.1.0.

## [Unreleased]: planned as v0.1.0

### Added

- **Contracts (M0):**
  - JSON Schemas for `run.json`, `events.jsonl`, `labels.json`, bug reports, visual findings and the LLM draft (`schemas/`);
  - valid and invalid examples, and a hand-made sample run (`samples/sample_run`);
  - contract tests proving the schemas and the pydantic models accept and reject the same inputs.
- **`qalab` CLI (M0–M3):** `validate`, `triage run`, `triage clusters`, `report html`, and `qalab.toml` configuration.
- **Triage (M2):**
  - run loading with per-line validation;
  - stack parsing and line-free signatures;
  - four clustering variants (`exact`, `frame_tfidf`, `frame_embed`, `tfidf_only`);
  - the explainable rank formula with P1–P4;
  - template reports.
- **AI reports (M3):**
  - a provider layer with `FakeProvider`, `OllamaProvider` and `GeminiProvider` (`[gemini]` extra, Google's `google-genai` SDK);
  - JSON-schema output at temperature 0, pydantic validation and 2 retries;
  - grounding checks on evidence, action and doc ids, with `needs_review` reasons;
  - a SQLite answer cache.
- **RAG (M3):** heading-aware chunks of design docs, a cached embedding index with TF-IDF fallback, and code context from the classes in stack traces.
- **Outputs:** self-contained `report.html` (filter, review-only toggle, evidence, score breakdown), `bugs.json`, `report.md`, `bugs_jira.csv`, `clusters.json`, `validation_report.json`, `triage_meta.json`.
- **Unity package `com.miawworks.qalab` 0.1.0 (M1):**
  - thread-safe `events.jsonl` writer, with `run_end` always last and no seq gaps;
  - `run.json` with clean-end detection;
  - log capture (threaded callback, level mapping, `[QALab]` filter) and metrics every second (fps, frame time average and p95, memory);
  - benchmark labels;
  - command-line flags, a settings asset and the Tools → QA Lab menu.
- **Sandbox (M1):**
  - seeded log bugs SB01, SB03, SB04, SB05, SB13 and SB14, with SB02 as a learning task;
  - an F1 trigger menu and a code-built scene generator;
  - a code seed catalog checked against the sample labels, with match rules checked to be disjoint on real stacks.
- **ProjectScanner (M7):**
  - missing scripts, broken and unassigned references, empty material slots, error shaders and missing Build Settings scenes;
  - writes `scan.json`; Tools → QA Lab → Scan Project, or `-executeMethod`;
  - `scripts/scan_project.ps1`.
- **Bot contracts (M7 polish):**
  - `IBotAdapter`, `BotContext`, `BotAdapterRegistry` (snake_case names, the same rule as `-qalabAdapter`) and `SeededRandom`;
  - a "Game adapter template" package sample whose decision rule, `TurnPolicy.Decide`, is the M7 learning task.
- **Bot, detectors, screenshots (M4):**
  - `BotRunner` (decisions every 0.25 s, the move target re-issued every frame, a failing adapter stops the bot but not the run);
  - built-in bots `navmesh_explorer` (reachable random targets, biased to less-visited cells) and `ui_crawler` (random uGUI clicks with a blocklist);
  - `DetectorHub` (rate limit per detector and 4 m cell, screenshot per report) with `fell_out_of_world` (respawns the player), `perf_spike`, `exception_burst`, the `TunnelingDetector` component, and `stuck` as the M4 learning task (skipped until written);
  - `ScreenshotService` (`ScreenCapture`, camera render in batch mode, long side ≤ 1280 px, F12), with visual labels from `IVisualSeed`s in benchmark runs;
  - `results.xml` (JUnit) and player exit codes 0/1/2; the commit stamped into builds;
  - `QALab.ReportDetector`, `QALab.KillPlaneY`, `QALab.RequestScreenshot`;
  - editor: `BuildRunner` and the QA Lab window.
- **Sandbox (M4):** seeds SB06–SB12, SB15 and SB16 (the south-east corridor over T_17, a too-narrow doorway, GcZone, a magenta crate, a camera blackout zone, an overflowing score, an empty ammo icon, a failing Settings Apply, a ballistics range), positions shared through `SandboxLayout`, and F1 buttons for each.
- **Triage evaluation (M5):**
  - `qalab eval triage`: pairwise precision/recall/F1 and cluster-count error per clustering variant (E1);
  - report quality (field completeness, grounding, repro-step match, severity agreement, component, retrieval hit@k, latency, tokens) for E2/E3;
  - JSON, Markdown tables and charts;
  - `scripts/benchmark.ps1` with a `manifest.json` (D-028).
- **Vision (M6):**
  - `qalab vision analyze` with four methods: heuristics (black ratio, magenta ratio, white boxes), a VLM through the provider layer, an optional logistic-regression baseline, and a hybrid;
  - findings become `visual:<label>` bugs in triage, with the best screenshot first;
  - `qalab vision dataset` (split by run), `qalab vision train-ml`;
  - `qalab eval vision`: per-label P/R/F1, FP/100 frames, VLM calls, latency and cost, tuned on val and scored on test (D-029);
  - a non-fatal vision step in `run_pipeline.ps1` and the Jenkinsfile.
- **Learning tasks written (D-030):** `normalize_message`, `cosine_top_k`, `pairwise_prf`, `magenta_ratio`, `hello_events`, SB02, the stuck detector and `TurnPolicy.Decide`, so triage, eval and vision run end to end from a fresh clone.
- **One-command setup:** `scripts/setup.ps1` creates the venv, installs qalab (optionally with the Gemini SDK), creates `.env` from the template, validates the sample run and runs the tests.
- **Engine-free C# checked outside Unity:** `tools/cs-check` builds it as netstandard2.1 / C# 9 and runs its NUnit tests on .NET 8.
- **Scripts and CI:**
  - `find_unity.ps1`, `unity_tests.ps1`, `link_sandbox_package.py`;
  - `build_sandbox.ps1`, `run_playtest.ps1` and `run_pipeline.ps1` (build → bot playtest + menu crawl → triage → report, M4);
  - GitHub Actions running ruff, pytest on Windows and Ubuntu, a smoke triage with the fake provider, and cs-check;
  - an example `ci/Jenkinsfile`.
- **Docs:** `USER_GUIDE.md`, `GAME_INTEGRATION.md`, `ARCHITECTURE.md`, decisions D-001–D-030, and a single PC checklist (`docs/progress/PC_CHECKLIST.md`).

### Changed

- Project metadata names the publisher, Miaw Works, with its website (miaw-works.dev) and contact email:
  README header and contact section, `pyproject.toml` authors and URLs, Unity `package.json` author and
  `documentationUrl`.
- The sample run's SB06 fall moved to tile T_17's real position, (35, 1) (D-022, D-026), and its SB06/SB08 events now carry what the detectors write (`kill_plane_y`, `threshold_ms`).

### Not built yet

- M4 on the PC: the first Unity run of the bot, detectors and screenshots, and the screenshot spike.
- M5 on the PC: recording the 20-seed benchmark and the E1–E3 numbers.
- M6 on the PC: the vision dataset from the benchmark and the H1–H4 numbers.
- M7 on the PC: real-game integration, demo video, measured README results.
