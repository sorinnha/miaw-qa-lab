# Changelog

All notable changes to this package. Format: [Keep a Changelog](https://keepachangelog.com/).

## [Unreleased]: planned as 0.1.0

### Added
- Run folder writer: `run.json`, thread-safe `events.jsonl` (logs, metrics, markers), `labels.json`
  in benchmark mode.
- Command-line flags and a `QALabSettings` asset for Play Mode.
- Log capture with `LogType` → level mapping, metrics every second (fps, frame time avg/p95, memory).
- `EventWriter.Close`: `run_end` is the last event, with no `seq` gaps at the end of a run (it waits up
  to 1 s for log calls already in progress; later logs are dropped).
- ProjectScanner (Tools > QA Lab > Scan Project, or `-executeMethod MiawWorks.QALab.Editor.ProjectScanner.RunFromCommandLine`):
  missing scripts, broken and unassigned references, empty material slots, error shaders, missing Build
  Settings scenes; writes `scan.json`, exit 1 on errors.
- Bot contracts for adapters: `IBotAdapter`, `BotContext` (`LogAction`), `BotAdapterRegistry` (snake_case
  names, the same rule as `-qalabAdapter`), `SeededRandom`.
- `BotRunner` and the built-in bots `navmesh_explorer` and `ui_crawler` (the latter two need the AI
  module and uGUI; version defines keep the package compiling without them).
- Detectors through `DetectorHub` (rate-limited, one screenshot per report): `fell_out_of_world`,
  `perf_spike`, `exception_burst`, `tunneling` (`TunnelingDetector` component) and `stuck`
  (`StuckCalculator`/`StuckDetector`, stubs until the learning task is done; skipped at runtime).
  Games report their own with `QALab.ReportDetector`.
- Screenshots (`shots/NNNNNN.png`, periodic, per detector event, F12), visual labels for benchmark runs
  (`IVisualSeed`, `VisualLabelProbe`), `results.xml` (JUnit) and exit codes 0/1/2 with `-qalabQuitOnEnd`.
- Editor: `BuildRunner` (`-executeMethod MiawWorks.QALab.Editor.BuildRunner.BuildSandboxPlayer`, commit
  stamped into the build) and the QA Lab window (Tools > QA Lab > Window).
- Sample "Game adapter template": a turn-based game adapter driven through the game's own commands.
