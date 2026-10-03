# Miaw QA Lab

AI-assisted game QA for Unity. A seeded autoplay bot explores your game and catches bugs, and a Python tool turns the logs and screenshots into ranked, evidence-backed bug reports.

> **Status:** in development. See [docs/PLAN.md](docs/PLAN.md) for the roadmap and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the design.

- **Unity package** (`unity/com.miawworks.qalab`, C#): event logging, metrics, screenshots, a seeded bot (NavMesh explorer, UI crawler, game adapters), and detectors for falls, stuck players, frame spikes and tunnelling.
- **`qalab` CLI** (`python/`): deduplicates and ranks errors, then drafts Jira-ready reports with a local LLM and RAG over design docs and code. Every claim cites evidence.
- **Vision:** finds missing textures, black screens and broken UI using heuristics, a VLM, or both.
- **Benchmark:** 16 seeded bugs in a sandbox project, so every result is measured.

The full README (demo, quick start, results, data handling, how it was built) is written in milestones M3 and M7.

License: MIT
