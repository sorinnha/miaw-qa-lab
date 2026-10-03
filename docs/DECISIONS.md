# Decisions

One entry per real choice: what we decided, why, what else we considered, and what it costs. Newest at the bottom.

## D-001 · 2026-10-03 · Monorepo: Unity package + Python CLI + shared schemas
- **Why:** one pipeline, one README, one CI; the schemas sit next to both sides that use them.
- **Alternatives:** separate repos per module (more pinned repos, but duplicated contracts and harder end-to-end demos).
- **Consequences:** games install the package by git URL with `?path=/unity/com.miawworks.qalab`.

## D-002 · 2026-10-03 · Local model (Ollama) as the default provider; providers stay pluggable
- **Why:** free, works offline, and unreleased game data stays on the machine. Some hosted free tiers may use submitted content to improve their products.
- **Alternatives:** hosted APIs (often better quality, but cost and data-handling trade-offs).
- **Consequences:** smaller models need stricter validation and grounding. E2 measures the quality gap.

## D-003 · 2026-10-03 · Seeded-bug benchmark with labels from engine state
- **Why:** precision and recall without manual labelling; repeatable across seeds.
- **Alternatives:** hand-labelled runs only (slow, small).
- **Consequences:** labels must never reach inference code (enforced by a test). We also need a small real-game set (M7) to check the results transfer.

## D-004 · 2026-10-03 · Priority is computed, severity is suggested
- **Why:** QC leads need an explainable, stable ordering; LLM output can vary.
- **Consequences:** a severity/priority disagreement greater than one level sets `needs_review`.

## D-005 · 2026-10-03 · Playtests run as a windowed development player
- **Why:** that's what QC tests; full-fidelity screenshots including overlay UI.
- **Alternatives:** editor batch-mode PlayMode tests (great for CI of the framework, but no overlay UI in captures).
- **Consequences:** the build step is part of the pipeline. Confirm with the M4 screenshot spike.

## D-006 · 2026-10-03 · Code can be built in cloud sessions; engine-free C# is tested outside Unity
- **Why:** a claude.ai/code cloud session (Linux, no Unity or Ollama) can build and test most of the code while Sora's PC stays free. What it can't check is listed, not hidden.
- **How:** one milestone per cloud session and draft PR (CLAUDE.md → Cloud mode). Unity, Ollama and PowerShell steps go in the PR's PC checklist. Engine-free C# is compiled and unit-tested with .NET 8 in `tools/cs-check`, also as a CI job. YOU WRITE tests are marked `youwrite` and stay xfail until Sora writes the function.
- **Alternatives:** build everything locally (slower, and the PC is busy); let the cloud write the YOU WRITE solutions (defeats the point of the project).
- **Consequences:** the pipeline can't run end to end until Sora finishes the YOU WRITE tasks it depends on. Code that calls Unity APIs is verified only on the PC.
