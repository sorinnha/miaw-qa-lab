# Architecture

Claude Code keeps this page current, and adds a proper diagram image in M7.

```
 Unity game or QALabSandbox ─ package com.miawworks.qalab (C#)
 ┌──────────────────────────────────────────────────────────────────┐
 │ BotRunner ── IBotAdapter: NavMeshExplorer · UICrawler · GameAdapter│
 │              SeededRandom(seed) → action events (repro steps)     │
 │ LogCapture · MetricsSampler · DetectorHub (stuck, fall, perf,     │
 │ exception burst, tunneling) · ScreenshotService                   │
 │ LabelRecorder + VisualLabelProbe (benchmark mode only)            │
 └───────────────┬──────────────────────────────────────────────────┘
                 │  runs/<run_id>/ run.json · events.jsonl · shots/*.png
                 │                 results.xml · labels.json (benchmark only)
                 ▼
 Python package qalab
   qalab vision analyze ──► visual_findings.jsonl ──┐
                                                    ▼
   qalab triage run:
     validate → normalize → parse stacks → signature → cluster → rank
       → context (events, bot actions, logs, RAG docs, code)
       → LLM draft (JSON schema) → validate + grounding checks → template fallback
       → reports/<name>/ bugs.json · report.md · bugs_jira.csv · report.html
   qalab eval (triage | vision) ◄── labels.json ──► docs/EVAL_RESULTS.md

 CI: GitHub Actions (ruff, pytest, smoke run with FakeProvider)
     ci/Jenkinsfile (example nightly: Unity tests → build → playtests → triage → publish)
```

## Components

| Component | Language | Responsibility | Spec |
|---|---|---|---|
| Event writer, log capture, metrics | C# | Thread-safe JSONL event stream | 01 |
| Bot + adapters | C# | Seeded, reproducible exploration; action log | 01 |
| Detectors | C# | Gameplay-level bug signals (falls, stuck, perf, tunneling) | 01 |
| Screenshots + labels | C# | Frames for vision; ground truth from engine state | 01 |
| Project scanner | C# (Editor) | Static checks: missing scripts, broken references, null materials | 01 |
| Triage | Python | Deduplicate, rank and explain bugs with evidence | 02 |
| RAG | Python | Design-doc and code context for reports | 02 |
| LLM layer | Python | Pluggable providers, structured output, cache | 02 |
| Vision | Python | Heuristics / VLM / ML / hybrid glitch detection | 03 |
| Eval | Python | Benchmarks against seeded ground truth | 02, 03 |
| Scripts + CI | PowerShell, YAML, Groovy | One-command pipeline, CI, releases | 04 |

## Design principles

1. **Contracts first.** The schemas decouple Unity and Python, and both sides test against the same examples.
2. **Works without AI.** Template reports always work. The LLM improves them but is never required.
3. **Evidence or it didn't happen.** Every report field traces to events, actions, docs or code.
4. **Measure.** Seeded bugs give ground truth; every claim in the README comes from an eval script.
5. **Local by default.** Unreleased game data stays on the machine unless the user chooses otherwise.

See `docs/DECISIONS.md` for the reasoning behind specific choices.
