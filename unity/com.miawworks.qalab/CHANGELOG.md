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
  names, the same rule as `-qalabAdapter`), `SeededRandom`. The bot runner that calls them comes in a later version.
- Sample "Game adapter template": a turn-based game adapter driven through the game's own commands.
