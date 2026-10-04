# Interview prep: Junior R&D Engineer (Gen AI/ML), QC automation

Fill the `[ ]` placeholders from `EVAL_RESULTS.md` and `REAL_GAME.md` as results come in. Never quote a number you didn't measure.

Until M4–M6 are done, pitch only what is built. The section "What's actually built" below lists it, with file paths.

## 60-second pitch (practise out loud)

> I built Miaw QA Lab, a small version of a QC automation stack for Unity.
>
> A C# package records logs, metrics and screenshots, and runs a seeded bot that explores levels and clicks through menus. Detectors catch falls through the world, stuck players, frame spikes and tunnelling.
>
> A Python tool groups thousands of log lines into unique bugs, ranks them with an explainable score, and uses an LLM (Gemini, or a local model when the data is unreleased) with RAG over design docs and code to draft Jira-ready reports. Every field cites evidence, and reproduction steps come from the bot's action log, so the model can't invent them.
>
> A vision module flags missing textures, black screens and broken UI with heuristics and a VLM.
>
> To measure it honestly I seeded 16 known bugs. Clustering reaches [F1] and visual detection [recall]. On my own game it found [N] real issues.

## What's actually built (as of 2026-10-04)

These are things you can open and explain. Decisions are in `docs/DECISIONS.md` (D-xxx).

| Area | Files | What to say | Decisions |
|---|---|---|---|
| Data contracts | `schemas/*.json`, `samples/sample_run/`, `python/tests/models/test_contracts.py` | Unity and Python share only these schemas; both sides test against the same examples | D-001, D-007 |
| Loading runs | `python/src/qalab/io/runs.py` | Validates raw JSON first so errors name the schema rule; bad lines are reported, not fatal; no `ended_at` → suspected crash | D-007, D-014 |
| Normalize, stacks, signatures | `triage/normalize.py` (yours), `triage/stack.py`, `triage/signature.py` | Message + top 3 app frames, no line numbers; why SB01 and SB04 stay apart | D-009 |
| Clustering | `triage/cluster.py` | Exact signatures, then optional TF-IDF/embedding merges with union-find; four variants for E1 | D-009, D-013 |
| Ranking | `triage/rank.py`, `qalab.toml [rank]` | Explainable formula, priority computed, never from the LLM | D-004, D-012 |
| Context + RAG | `triage/context.py`, `rag/chunk.py`, `rag/index.py`, `rag/retrieve.py` (`cosine_top_k` is yours), `rag/code_context.py` | E/A/L/D ids, a character budget, heading chunks, TF-IDF fallback | D-009, D-011 |
| LLM layer | `llm/base.py`, `llm/fake.py`, `llm/ollama.py`, `llm/gemini.py`, `llm/cache.py`, `llm/factory.py` | Protocol (like a C# interface), JSON-schema output, temperature 0, retries, SQLite cache | D-002, D-008, D-021 |
| Reports | `triage/report_llm.py`, `triage/report_template.py`, `report/html.py`, `report/jira_csv.py` | Grounding checks drop unknown ids and flag `needs_review`; template fallback; offline HTML | D-010, D-013 |
| CLI | `python/src/qalab/cli.py` | typer; exit 0 / 3 on P1 / 2 on error | — |
| Unity event writer | `unity/com.miawworks.qalab/Runtime/Core/EventWriter.cs`, `QALabHost.cs`, `MainThreadCache.cs` | Any thread enqueues; the main thread drains every 0.5 s and sorts by `seq`; `Close` seals, waits for in-flight producers, writes `run_end` last | D-022, D-023 |
| Log capture + metrics | `Runtime/Logging/LogCapture.cs`, `Runtime/Metrics/MetricsSampler.cs`, `RingBuffer.cs` | Threaded callback, `[QALab]` filter, p95 from a preallocated ring buffer, no per-frame allocations | D-022 |
| Seeded sandbox | `unity/QALabSandbox/Assets/Sandbox/Scripts/SeededBugs/*`, `SandboxSeedCatalog.cs` | Real NullReferenceExceptions from plain objects; one code path per seed; catalog rules checked to be disjoint | D-017, D-020, D-023 |
| ProjectScanner | `Editor/Scanner/ProjectScanner.cs`, `Editor/Scanner/ScanReport.cs`, `scripts/scan_project.ps1` | Missing vs unassigned reference via instance id; scenes opened additively and closed; exit codes for CI | D-024 |
| Bot contracts | `Runtime/Bot/IBotAdapter.cs`, `BotContext.cs`, `BotAdapterRegistry.cs`, `SeededRandom.cs`, `Samples~/GameAdapterTemplate/` | Strategy + registry; seeded RNG; action events become repro steps | D-024 |
| C# outside Unity | `tools/cs-check/` | netstandard2.1 + C# 9 like Unity, NUnit on .NET 8 in CI; what it does and doesn't prove | D-006, D-018 |
| CI | `.github/workflows/python-ci.yml`, `scripts/ci_smoke.py`, `ci/Jenkinsfile` | Windows + Ubuntu, smoke triage with the fake provider; Jenkinsfile is an example | D-015, D-016 |

**Not built yet (don't claim it):**
- the bot runner and built-in adapters, detectors, screenshots, JUnit output, `run_pipeline.ps1` (M4);
- evaluation numbers (M5);
- vision (M6);
- the real-game run (M7).

## Job description → your evidence

| They ask for | Show them |
|---|---|
| Automation tools, frameworks, prototypes integrated into testing systems | UPM package installed by git URL; adapters; NUnit/JUnit output; Jira CSV; `run_pipeline.ps1` |
| Automation tests, engine-level utilities, early AI/ML tools | PlayMode smoke tests + bot; ProjectScanner + QA Lab window; triage + vision |
| Debug, troubleshoot, refine for QC usability | `needs_review` + reasons, HTML report, USER_GUIDE.md, your hardest-bug story |
| Iterate with feedback | CHANGELOG, change log in PLAN.md, a friend testing the HTML report and what you changed |
| Document findings | DECISIONS.md, EVAL_RESULTS.md, LEARNING.md |
| Evaluate new approaches | E1–E3 and H1–H4 experiments with hypotheses written first |
| Prototype → validated component in a real environment | Spikes → modules → run on Crimson Tactics |
| Python, AI/ML, GenAI/RAG, SDLC/STLC, debugging, Git | The whole repo, plus commit history on a branch per milestone |
| Nice: C#/C++, CI/CD (Jenkins/TeamCity) | Unity package; GitHub Actions; `ci/Jenkinsfile` |

## Questions to be ready for (with answer hints)

### Unity package

1. *Why does the log callback only enqueue?* It can run on any thread; Unity APIs aren't thread-safe. Scene, frame and position are cached on the main thread and the queue is flushed there.
2. *How do you keep events in order across threads?* `Interlocked.Increment` for `seq`, a sort before writing, and Python sorts again.
3. *Why your own seeded RNG?* Reproducibility, and `UnityEngine.Random` shares global state with the game.
4. *Is the bot deterministic?* Its decisions are; physics and frame timing aren't. The action log is the repro record (replay mode is the next step).
5. *Why skip perf-spike frames where you captured a screenshot?* The tool causing the spike would be a false positive: the observer effect.
6. *Missing vs unassigned reference?* Missing = broken link (instance id ≠ 0, object null). Unassigned = never set (id 0).
7. *Why did the player fall through a "walkable" tile?* The NavMesh was baked from render meshes, but the tile had no collider.
8. *Tunnelling?* Discrete collision checks positions per physics step, so fast objects skip thin colliders. Fixes: continuous collision, thicker colliders, swept raycasts, a lower speed or a smaller timestep.
9. *How do the Unity tests run from the command line?* `-runTests -batchmode -testPlatform PlayMode -testResults x.xml`, with NUnit XML output and no `-quit`.
10. *Why a built player rather than the editor for playtests?* QC tests builds; overlay UI is captured; the editor isn't the shipping runtime.

### Triage

11. *Why normalize messages, and what's the risk?* Numbers and ids make one bug look like many; over-normalizing merges different bugs (paths, ids).
12. *Why stack frames in the signature but not line numbers?* Frames separate bugs with the same message (SB01 vs SB04); line numbers change between builds.
13. *Exact vs TF-IDF vs embeddings?* Cost, explainability, threshold tuning. Show E1, including why `tfidf_only` wrongly merged SB01 and SB04.
14. *Why that ranking formula?* Log of count stops spam dominating; spread across runs means it reproduces; severity weights are explainable and tunable.
15. *How do you stop the LLM inventing repro steps?* Steps must cite bot actions or be marked inferred; grounding checks; `needs_review`; priority isn't from the LLM.
16. *Why JSON schema output + validation + retries + temperature 0 + cache?* Parseable, repeatable and cheap, and failures degrade to the template.
17. *What does RAG add, and when does it hurt?* Expected behaviour and component names; irrelevant chunks can mislead (E3). Hence a minimum score and doc_ids citations.
18. *Local vs hosted model?* Privacy of unreleased builds (some free tiers use your data), cost, latency, quality. Show E2.
19. *How would you scale to a million events a day?* Stream, aggregate by signature first, call the LLM only for new or changed clusters (cache keyed by signature), queue the work, store in a database.
20. *How do you evaluate report quality without humans?* Grounding rate, field completeness, repro-step match, severity agreement, plus a small human-rated sample.

### Vision

21. *Why split the dataset by run, not by frame?* Consecutive frames are near-duplicates, so a frame split leaks and inflates scores.
22. *Precision or recall?* Recall for blockers (don't miss them); precision to avoid alert fatigue. It depends on the cost of each error.
23. *Why heuristics before the VLM?* Cheap, fast and explainable; the VLM covers what pixels-math can't (ui_overflow). Show the hybrid trade-off (H3).
24. *VLM failure modes?* Hallucinated glitches, confusing art style for bugs, prompt sensitivity. Mitigations: strict labels, examples in the prompt, thresholds, human review.

### Python, QA, Git, CI

25. Generators and why they matter for big logs. `with` vs C# `using`. `Protocol` vs interfaces. pytest fixtures and monkeypatch.
26. SDLC vs STLC; smoke vs sanity vs regression; severity vs priority; the bug lifecycle.
27. *Flaky tests?* Seeds, retries with logging, quarantine, fix the root cause. The bot's nondeterminism is a real example.
28. Merge vs rebase; why a branch per milestone; Conventional Commits.
29. *What does your Jenkinsfile do, and when does it fail the build?* Exit code 3 means a P1 bug.

### Things that happened while building it

31. *How do you end a run without losing a log written on another thread?* Producers increment an in-flight counter, then check a "sealed" flag. `Close` sets the flag, then waits for the counter to reach 0. Both sides use `Interlocked` (a full fence) before reading the other's value, so either `Close` sees the producer or the producer sees the seal. Then `run_end` gets the last seq. Know the 1 s cap and why it exists.
32. *How did you find out your ground truth was wrong?* A review found that SB03's real stack ran through `SpawnWave`, so it also matched SB04's match rule: one exception would have counted as two bugs. The fix moved target assignment to `Update`, and a test now checks that every seed's real event matches only its own rule.
33. *A test was flaky in CI. What did you do?* A multi-threaded test assumed the producers would still be running when the drain started. I reproduced it with a forced delay and rewrote it with a handshake (no timing assumptions); it passed 40/40.
34. *Why test Unity C# outside Unity?* Fast feedback in CI with no license. netstandard2.1 + C# 9 catches APIs Unity doesn't have. It can't check Unity APIs, scenes or serialization, so those stay as EditMode/PlayMode tests.
35. *Missing vs unassigned in your scanner, and why unassigned is only info?* Instance id ≠ 0 with a null value means a deleted target; id 0 means never set, often optional. Built-in components have many optional slots, so only your own scripts get the info line.

### C++ refresh (they list it as nice-to-have)

36. RAII, `unique_ptr` vs `shared_ptr`, references vs pointers, virtual functions and vtables, `std::map` vs `std::unordered_map` (asked in a past R&D interview).

## Design patterns you used (covers the gap from your last interview)

| Pattern | Where in QA Lab |
|---|---|
| Strategy | `IBotAdapter` (contract built, runner in M4), `LLMProvider`, clustering variants |
| Adapter | Game adapters wrap a game's own commands for the bot |
| Observer | Unity log callback, `activeSceneChanged` (built); detectors reporting to `DetectorHub` (M4) |
| Factory / Registry | `BotAdapterRegistry` over `NamedRegistry<T>`, `llm/factory.py` |
| Facade | `QALab` static API |
| Producer–consumer | Event queue → main-thread flush |
| Fallback / Null Object | Template report when the LLM fails; FakeProvider in tests |
| Singleton (with care) | The single `DontDestroyOnLoad` QALab object. Know the downsides: hidden global state, harder tests |

## STAR stories to prepare (write them in LEARNING.md)

1. The hardest bug you hit while building this, and how you found it. Real candidates from this repo: the run-end race (Q31), the SB03/SB04 rule overlap (Q32), the flaky thread test (Q33), and terminal colour codes breaking the CI smoke check on Ubuntu (`scripts/ci_smoke.py`).
2. A decision you made from data (E1, E3 or H3).
3. Learning Python quickly while shipping.
4. Something that didn't work (for example, VLM false positives) and what you changed.

## Being honest about AI help

> I used Claude Code as a pair programmer. I designed the milestones and contracts with it, wrote [the normalizer, cosine retrieval, clustering metrics, magenta heuristic, stuck detector, game adapter] myself, and reviewed every change. I can walk you through any file.

## Questions to ask them

- How does the team choose which manual tests to automate first?
- Do your tools hook into the engine like a test framework, or work black-box on builds?
- How do you validate a GenAI tool before QC teams rely on it?
- What would a junior own in their first three months?
