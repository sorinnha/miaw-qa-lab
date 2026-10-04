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
- Other games: `"com.miawworks.qalab": "https://github.com/sorinnha/miaw-qa-lab.git?path=/unity/com.miawworks.qalab#v0.2.0"`.

Register the player once so events carry a position: `QALab.RegisterPlayer(transform, mover)`.

## Flags

`-qalab`, `-qalabOut <dir>`, `-qalabSeed <int>`, `-qalabDuration <s>`, `-qalabAdapter <name>`,
`-qalabScene <name>`, `-qalabShotEvery <s>`, `-qalabMinLevel <lvl>`, `-qalabSeeds <list>`,
`-qalabBenchmark`, `-qalabQuitOnEnd`. Defaults and meaning: spec 01, "Activation and configuration".

## Guarantees and limits

- Thread-safe: log callbacks from any thread only enqueue; the main thread writes every 0.5 s.
- `seq` is assigned when an event is created, so lines can be slightly out of order; readers sort
  by `seq` (the Python tools do).
- Crash tolerance: at most about 0.5 s of events can be lost if the process dies. `run.json` gets
  `ended_at` only on a clean end, which is how triage spots a possible crash.
- QA Lab's own logs start with `[QALab]` and are never captured.
