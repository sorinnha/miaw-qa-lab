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
- **Bot contracts (M7 polish):** `IBotAdapter`, `BotContext`, `BotAdapterRegistry`, `SeededRandom`, and a "Game adapter template" package sample.
- **Engine-free C# checked outside Unity:** `tools/cs-check` builds it as netstandard2.1 / C# 9 and runs its NUnit tests on .NET 8.
- **Scripts and CI:**
  - `find_unity.ps1`, `unity_tests.ps1`, `link_sandbox_package.py`;
  - GitHub Actions running ruff, pytest on Windows and Ubuntu, a smoke triage with the fake provider, and cs-check;
  - an example `ci/Jenkinsfile`.
- **Docs:** `USER_GUIDE.md`, `GAME_INTEGRATION.md`, `ARCHITECTURE.md`, decisions D-001–D-024, and a single PC checklist (`docs/progress/PC_CHECKLIST.md`).

### Not built yet

- M4: bot runner, NavMesh explorer and UI crawler, detectors, screenshots, JUnit results, build/playtest/pipeline scripts.
- M5: triage evaluation on the 20-seed benchmark.
- M6: vision.
- M7 on the PC: real-game integration, demo video, measured README results.
