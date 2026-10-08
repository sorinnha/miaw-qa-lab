# QA Lab (Unity package)

By [Miaw Works](https://miaw-works.dev) · miaw@miaw-works.dev

Writes a QA Lab run folder during a playtest (contract: `docs/specs/00_contracts.md` in the
repository), optionally with a seeded bot playing and detectors watching. Enable it with `-qalab` on
the player's command line, or with the QA Lab window (Tools > QA Lab > Window, "Play with QA Lab") in
the editor.

```
runs/<run_id>/run.json        metadata (rewritten with ended_at, exit code on a clean end)
runs/<run_id>/events.jsonl    one event per line: logs, bot actions, detectors, screenshots, metrics, markers
runs/<run_id>/shots/          screenshots (periodic, per detector event, F12), long side ≤ 1280 px
runs/<run_id>/results.xml     JUnit summary for CI
runs/<run_id>/labels.json     benchmark runs only (-qalabBenchmark)
```

## Install

- Sandbox: `"com.miawworks.qalab": "file:../../com.miawworks.qalab"` in
  `unity/QALabSandbox/Packages/manifest.json`, plus `"testables": ["com.miawworks.qalab"]`.
- Other games: `"com.miawworks.qalab": "https://github.com/sorinnha/miaw-qa-lab.git?path=/unity/com.miawworks.qalab"`,
  pinned with `#<tag>` once releases exist (or `#<commit>`). Walkthrough: `docs/GAME_INTEGRATION.md`.

Register the player once so events carry a position and the bot can steer it:
`QALab.RegisterPlayer(transform, mover)`.

## Detectors

`fell_out_of_world` (critical; respawns the player; kill plane = `QALab.KillPlaneY` or the lowest
renderer − 5 m), `stuck` (major), `perf_spike` (minor, > 50 ms), `exception_burst` (major, > 20 in 1 s)
and `tunneling` (major; add `TunnelingDetector` to fast projectiles). Each detector reports a 4 m cell
at most once per 10 s, with a screenshot. Report your own with `QALab.ReportDetector(name, severity,
details)`. With `-qalabQuitOnEnd` the player exits 0 (clean), 1 (a blocker/critical finding) or 2 (QA
Lab failed).

## Project scanner

**Tools > QA Lab > Scan Project** checks the enabled Build Settings scenes and every prefab under
`Assets/` for missing scripts, broken references, empty material slots and error shaders, plus
unassigned script fields (as info). It writes `Logs/qalab/scan.json`. Batch mode:
`-executeMethod MiawWorks.QALab.Editor.ProjectScanner.RunFromCommandLine [-qalabScanOut <file>]`; exit
code 0 means clean, 1 means errors, 2 means the scan failed.

## Bot adapters

An adapter (`IBotAdapter`) is the bot's strategy. Built in: `navmesh_explorer` (needs a baked NavMesh
and a registered player) and `ui_crawler` (uGUI). Register your own by name with `BotAdapterRegistry`
and select it with `-qalabAdapter <name>`. The bot runner calls `Step` every 0.25 s with a
`BotContext`: the run's `SeededRandom`, the player and mover, and `LogAction` for repro steps. Import
the **Game adapter template** sample (Package Manager > QA Lab > Samples) to write one for your game.

Build a development player for playtests with
`-executeMethod MiawWorks.QALab.Editor.BuildRunner.BuildSandboxPlayer -qalabBuildOut <path.exe>`.

## Flags

`-qalab`, `-qalabOut <dir>`, `-qalabSeed <int>`, `-qalabDuration <s>`, `-qalabAdapter <name>`,
`-qalabScene <name>`, `-qalabShotEvery <s>`, `-qalabMinLevel <lvl>`, `-qalabSeeds <list>`,
`-qalabBenchmark`, `-qalabQuitOnEnd`. Defaults and meaning: spec 01, "Activation and configuration".

## Guarantees and limits

- Thread-safe: log callbacks from any thread only enqueue; the main thread writes every 0.5 s.
- Clean end: the run's last event is `run_end`, with the last `seq` and no gaps, even if another
  thread is logging at that moment: `EventWriter.Close` waits up to 1 s for a log call already in
  progress. Logs that start after the run ends are dropped.
- `seq` is assigned when an event is created, so lines can be slightly out of order; readers sort
  by `seq` (the Python tools do).
- Crash tolerance: at most about 0.5 s of events can be lost if the process dies. `run.json` gets
  `ended_at` only on a clean end, which is how triage spots a possible crash.
- QA Lab's own logs start with `[QALab]` and are never captured.
