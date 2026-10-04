# QA Lab (Unity package)

Writes a QA Lab run folder during a playtest (contract: `docs/specs/00_contracts.md` in the
repository). Enable it with `-qalab` on the player's command line, or with the `QALabSettings`
asset ("Auto-start in Play Mode") in the editor.

```
runs/<run_id>/run.json        metadata (rewritten with ended_at on a clean end)
runs/<run_id>/events.jsonl    one event per line: logs, metrics, markers
runs/<run_id>/labels.json     benchmark runs only (-qalabBenchmark)
```

## Install

- Sandbox: `"com.miawworks.qalab": "file:../../com.miawworks.qalab"` in
  `unity/QALabSandbox/Packages/manifest.json`, plus `"testables": ["com.miawworks.qalab"]`.
- Other games: `"com.miawworks.qalab": "https://github.com/sorinnha/miaw-qa-lab.git?path=/unity/com.miawworks.qalab"`,
  pinned with `#<tag>` once releases exist (or `#<commit>`). Walkthrough: `docs/GAME_INTEGRATION.md`.

Register the player once so events carry a position: `QALab.RegisterPlayer(transform, mover)`.

## Project scanner

**Tools > QA Lab > Scan Project** checks the enabled Build Settings scenes and every prefab under
`Assets/` for missing scripts, broken references, empty material slots and error shaders, plus
unassigned script fields (as info). It writes `Logs/qalab/scan.json`. Batch mode:
`-executeMethod MiawWorks.QALab.Editor.ProjectScanner.RunFromCommandLine [-qalabScanOut <file>]`; exit
code 0 means clean, 1 means errors, 2 means the scan failed.

## Bot adapters

An adapter (`IBotAdapter`) is the bot's strategy. Register one by name with `BotAdapterRegistry`
and select it with `-qalabAdapter <name>`. It gets a `BotContext` with the run's `SeededRandom` and
`LogAction` for repro steps. Import the **Game adapter template** sample (Package Manager > QA Lab >
Samples) to write one for your game. The bot runner that calls adapters comes in a later version (M4).

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
