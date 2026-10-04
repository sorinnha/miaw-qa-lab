# Changelog

All notable changes to this package. Format: [Keep a Changelog](https://keepachangelog.com/).

## [0.1.0] - unreleased

### Added
- Run folder writer: `run.json`, thread-safe `events.jsonl` (logs, metrics, markers), `labels.json`
  in benchmark mode.
- Command-line flags and a `QALabSettings` asset for Play Mode.
- Log capture with `LogType` → level mapping, metrics every second (fps, frame time avg/p95, memory).
- `EventWriter.Close`: `run_end` is always the last event, with no `seq` gaps at the end of a run.
