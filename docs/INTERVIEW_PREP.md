# Interview prep: Junior R&D Engineer (Gen AI/ML), QC automation

Fill the `[ ]` placeholders from `EVAL_RESULTS.md` and `REAL_GAME.md` as results come in. Never quote a number you didn't measure.

## 60-second pitch (practise out loud)

> I built Miaw QA Lab, a small version of a QC automation stack for Unity.
>
> A C# package records logs, metrics and screenshots, and runs a seeded bot that explores levels and clicks through menus. Detectors catch falls through the world, stuck players, frame spikes and tunnelling.
>
> A Python tool groups thousands of log lines into unique bugs, ranks them with an explainable score, and uses a local LLM with RAG over design docs and code to draft Jira-ready reports. Every field cites evidence, and reproduction steps come from the bot's action log, so the model can't invent them.
>
> A vision module flags missing textures, black screens and broken UI with heuristics and a VLM.
>
> To measure it honestly I seeded 16 known bugs. Clustering reaches [F1] and visual detection [recall]. On my own game it found [N] real issues.

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

### C++ refresh (they list it as nice-to-have)

30. RAII, `unique_ptr` vs `shared_ptr`, references vs pointers, virtual functions and vtables, `std::map` vs `std::unordered_map` (asked in a past R&D interview).

## Design patterns you used (covers the gap from your last interview)

| Pattern | Where in QA Lab |
|---|---|
| Strategy | `IBotAdapter`, `LLMProvider`, clustering variants |
| Adapter | Game adapters wrap a game's own commands for the bot |
| Observer | Unity log callback, `activeSceneChanged`, detectors reporting to `DetectorHub` |
| Factory / Registry | `BotAdapterRegistry`, `llm/factory.py` |
| Facade | `QALab` static API |
| Producer–consumer | Event queue → main-thread flush |
| Fallback / Null Object | Template report when the LLM fails; FakeProvider in tests |
| Singleton (with care) | The single `DontDestroyOnLoad` QALab object. Know the downsides: hidden global state, harder tests |

## STAR stories to prepare (write them in LEARNING.md)

1. The hardest bug you hit while building this, and how you found it.
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
