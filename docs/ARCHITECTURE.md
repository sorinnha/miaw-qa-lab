# Architecture

The system diagram (Mermaid) is in the [README, section 4](../README.md#4-architecture). Dashed boxes there
are planned. This page keeps the component list, the module boundaries on both sides and the design
principles. Reasons for specific choices are in `docs/DECISIONS.md`.

## Components

| Component | Language | Responsibility | Spec | Status |
|---|---|---|---|---|
| Event writer, log capture, metrics | C# | Thread-safe JSONL event stream, run.json | 01 | Built (M1) |
| Labels + seeded sandbox | C# | Ground truth from engine state (benchmark mode), SB01–SB16 | 01 | Built (M1, M4) |
| Bot contracts | C# | `IBotAdapter`, `BotContext`, `BotAdapterRegistry`, `SeededRandom` | 01 | Built (M7 polish) |
| Bot runner + built-in adapters | C# | Seeded exploration (NavMesh, UI crawler); action log | 01 | Built (M4), not yet run in Unity |
| Detectors | C# | Falls, stuck (YOU WRITE), perf spikes, exception bursts, tunneling; results.xml, exit codes | 01 | Built (M4), not yet run in Unity |
| Screenshots + visual labels | C# | Frames for vision; labels from engine state | 01 | Built (M4), not yet run in Unity |
| Project scanner | C# (Editor) | Missing scripts, broken references, null materials, error shaders | 01 | Built (M7) |
| Triage | Python | Deduplicate, rank and explain bugs with evidence | 02 | Built (M2–M3) |
| RAG | Python | Design-doc and code context for reports | 02 | Built (M3) |
| LLM layer | Python | Pluggable providers (fake, Ollama, Gemini), structured output, cache | 02 | Built (M3) |
| Vision | Python | Heuristics / VLM / ML / hybrid glitch detection; findings join triage as `visual:<label>` events | 03 | Built (M6), not yet run on real sandbox frames |
| Eval | Python | Benchmarks against seeded ground truth: `qalab eval triage` (E1–E3, D-028) | 02, 03 | Built (M5 triage, M6 vision), not yet run on a recorded benchmark |
| Scripts + CI | PowerShell, YAML, Groovy | Unity tests, scanner, build, playtest, one-command pipeline, benchmark, CI | 04 | Built (M4–M5) |

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

vision (inference, never labels.json): heuristics · vlm (provider + llm_vision schema) · ml · hybrid
  → analyze.analyze_run → <run>/visual_findings.jsonl → io.runs.load_run adds visual:<label> events

eval (the only package that opens labels.json):
  ground_truth.build_ground_truth (catalog match rules → event → seeded bug)
  triage_eval: triage.pipeline functions → score_clusters (metrics.pairwise_prf) · score_reports
  → eval/triage_<label>.json / .md / charts
  vision_dataset (split by run) → vision_eval: tune on val, score on test → eval/vision_<label>.*
```

See `docs/DECISIONS.md` for the reasoning behind specific choices.

## Unity package map (`unity/com.miawworks.qalab`)

```
Runtime/
  Core/     QALab (facade + bootstrap) · QALabHost (run lifecycle) · EventWriter · QAEvent · CommandLine
            RunInfo/RunContext (run.json) · MainThreadCache · Clock · QALabSettings
  Logging/  LogCapture (threaded callback) · LogLevels
  Metrics/  MetricsSampler · RingBuffer (p95)
  Labels/   LabelRecorder · LabelBook · SeedCatalogEntry · VisualLabels · IVisualSeed · VisualLabelProbe
  Bot/      IBotAdapter · BotContext · BotAdapterRegistry · NamedRegistry · SeededRandom · BotActionData · IBotMover
            BotRunner (tracking mover) · NavMeshExplorerAdapter · UICrawlerAdapter · BuiltInAdapters
  Detectors/ IDetector + DetectorFrame · DetectorHub · RateLimiter · StuckCalculator/StuckDetector (YOU WRITE)
            FallDetector · PerfSpikeDetector · ExceptionBurstCounter/Detector · KillPlane · TunnelingDetector (component)
  Capture/  Shots + ShotPlanner (engine-free) · ScreenshotService · ManualShotKey (F12)
  Results/  JUnitWriter (results.xml) · ExitCodes
Editor/     QALabWindow · QALabMenu · BuildRunner · RunSummary · EditorCommandLine
            Scanner/ProjectScanner (Unity side) · Scanner/ScanReport (engine-free)
Samples~/GameAdapterTemplate/   IGameCommands · TurnPolicy (YOU WRITE, tested in cs-check) · MyGameAdapter · MyGameQALabBootstrap
Tests/EditMode (engine-free ones also run in tools/cs-check) · Tests/PlayMode
```

Files marked "Engine-free" in their header have no `UnityEngine`/`UnityEditor` and are compiled by
`tools/cs-check` too (D-018). Everything else compiles only in Unity.

One frame of a run (D-025): `QALabHost.Update` refreshes the main-thread cache → `BotRunner.Tick`
(re-issues the move target; `Step` every 0.25 s) → `DetectorHub.Tick` (each detector gets a
`DetectorFrame`; reports are rate-limited and reserve a screenshot) → `ShotPlanner.TakeDue` (at most one
capture, at the end of the frame) → `MetricsSampler.Tick` (skips frames that include a capture) → a
drain every 0.5 s. `EndRun` stops the bot and writes `run_end`, run.json (exit code), results.xml and
labels.json.
