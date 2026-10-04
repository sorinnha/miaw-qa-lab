# Architecture

The system diagram (Mermaid) is in the [README, section 4](../README.md#4-architecture). Dashed boxes there
are planned. This page keeps the component list, the module boundaries on both sides and the design
principles. Reasons for specific choices are in `docs/DECISIONS.md`.

## Components

| Component | Language | Responsibility | Spec | Status |
|---|---|---|---|---|
| Event writer, log capture, metrics | C# | Thread-safe JSONL event stream, run.json | 01 | Built (M1) |
| Labels + seeded sandbox | C# | Ground truth from engine state (benchmark mode) | 01 | Built for log seeds (M1) |
| Bot contracts | C# | `IBotAdapter`, `BotContext`, `BotAdapterRegistry`, `SeededRandom` | 01 | Built (M7 polish) |
| Bot runner + built-in adapters | C# | Seeded exploration (NavMesh, UI crawler); action log | 01 | Planned (M4) |
| Detectors | C# | Falls, stuck, perf spikes, exception bursts, tunneling | 01 | Planned (M4) |
| Screenshots + visual labels | C# | Frames for vision; labels from engine state | 01 | Planned (M4) |
| Project scanner | C# (Editor) | Missing scripts, broken references, null materials, error shaders | 01 | Built (M7) |
| Triage | Python | Deduplicate, rank and explain bugs with evidence | 02 | Built (M2–M3) |
| RAG | Python | Design-doc and code context for reports | 02 | Built (M3) |
| LLM layer | Python | Pluggable providers (fake, Ollama, Gemini), structured output, cache | 02 | Built (M3) |
| Vision | Python | Heuristics / VLM / ML / hybrid glitch detection | 03 | Planned (M6) |
| Eval | Python | Benchmarks against seeded ground truth | 02, 03 | Planned (M5–M6) |
| Scripts + CI | PowerShell, YAML, Groovy | Unity tests, scanner, CI; pipeline scripts in M4 | 04 | Partly built |

## Design principles

1. **Contracts first.** The schemas decouple Unity and Python, and both sides test against the same examples.
2. **Works without AI.** Template reports always work. The LLM improves them but is never required.
3. **Evidence or it didn't happen.** Every report field traces to events, actions, docs or code.
4. **Measure.** Seeded bugs give ground truth; every claim in the README comes from an eval script.
5. **The user decides where data goes.** The configured provider (Gemini, D-021) is for sandbox data; real-game runs use `--provider none` or a local model, so unreleased game data stays on the machine.

## Python module boundaries (triage side)

```
io.runs.load_run ──► LoadedRun{run, events sorted by seq, report}
      │
      ▼  (M2: normalize → stack → signature → cluster → rank)
triage.cluster.Cluster ──► triage.context.build_context ──► ClusterContext (E/A/L/D ids)
      │                        ▲ docs: rag.retrieve            │
      │                        ▲ code: rag.code_context        ▼
      │                                     triage.report_llm (prompt → provider → grounding)
      │                                     triage.report_template (fallback, --provider none)
      ▼                                                        │
models.bug.BugReport ◄─────────────────────────────────────────┘
llm: base.LLMProvider ← fake | ollama | gemini; cache.CachedProvider wraps them all.
```

See `docs/DECISIONS.md` for the reasoning behind specific choices.

## Unity package map (`unity/com.miawworks.qalab`)

```
Runtime/
  Core/     QALab (facade + bootstrap) · QALabHost (run lifecycle) · EventWriter · QAEvent · CommandLine
            RunInfo/RunContext (run.json) · MainThreadCache · Clock · QALabSettings
  Logging/  LogCapture (threaded callback) · LogLevels
  Metrics/  MetricsSampler · RingBuffer (p95)
  Labels/   LabelRecorder · LabelBook · SeedCatalogEntry
  Bot/      IBotAdapter · BotContext · BotAdapterRegistry · NamedRegistry · SeededRandom · BotActionData · IBotMover
Editor/     QALabMenu · Scanner/ProjectScanner (Unity side) · Scanner/ScanReport (engine-free)
Samples~/GameAdapterTemplate/   IGameCommands · MyGameAdapter · MyGameQALabBootstrap
Tests/EditMode (engine-free ones also run in tools/cs-check) · Tests/PlayMode
```

Files marked "Engine-free" in their header have no `UnityEngine`/`UnityEditor` and are compiled by
`tools/cs-check` too (D-018). Everything else compiles only in Unity.
