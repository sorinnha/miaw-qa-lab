# Spec 00: Data contracts

These files are the only interface between Unity (C#) and the Python tools. Both sides follow them exactly. `schemas/*.json` (JSON Schema 2020-12) are the source of truth; this page explains them.

## Run folder

Each playtest writes one folder:

```
runs/<run_id>/
  run.json             metadata (written at start, rewritten at end)
  events.jsonl         one event per line, append-only
  shots/000001.png     screenshots, numbered by a shot counter
  labels.json          ground truth (benchmark runs only; eval only)
  results.xml          JUnit summary written by the player (M4)
  player.log           Unity player log (-logFile)
  visual_findings.jsonl  added by `qalab vision analyze` (M6)
```

- `run_id` is Windows-safe: `<UTC yyyyMMddTHHmmssZ>-s<seed>`, e.g. `20261005T103000Z-s42`.
- Paths inside files are relative to the run folder and use `/`.
- Text files are UTF-8 without BOM, LF line endings.

## run.json (`qalab.run/1`)

Written when the run starts and again when it ends. If `ended_at` is missing or null, the run didn't finish cleanly. Triage flags the last errors before the end as a possible crash.

Key fields: `seed`, `adapter`, `mode` (`player` | `editor_playmode` | `manual`), `build` (version, platform, Unity version, git sha, development flag), `scenes`, `duration_s`, `exit_reason`, `exit_code`, `benchmark`, `seeds_enabled`.

## events.jsonl (`qalab.event/1`)

Common fields on every line: `schema`, `run_id`, `seq` (0, 1, 2… no gaps), `t` (seconds since start), `kind`. Optional: `ts`, `frame`, `scene`, `pos`, `level`, `message`, `stack`, `data`.

| kind | Required | `data` payload |
|---|---|---|
| `log` | `level`, `message` (`stack` may be null) | none |
| `action` | `data.action`, `data.step` | `adapter`, `target` [x,y,z], `ui_path`, `args` |
| `detector` | `data.detector`, `data.severity` | `details`, `screenshot` |
| `metric` | `data` | `fps`, `frame_ms`, `frame_ms_p95`, `mem_mb`, `gc_mb` (numbers only) |
| `screenshot` | `data.path`, `data.reason` | `w`, `h`, `method` (`screen_capture` / `camera_render`) |
| `marker` | `data.marker` | `details` |

Levels map Unity's `LogType`: Log→`info`, Warning→`warning`, Error→`error`, Exception→`exception`, Assert→`assert`.

Detector names are snake_case: `stuck`, `fell_out_of_world`, `perf_spike`, `exception_burst`, `tunneling`. Python adds `visual:<label>` events when it converts vision findings.

Detector severity words (`blocker` … `trivial`) describe the detector signal. Report severity (`S1`–`S4`) and priority (`P1`–`P4`) are decided later by triage.

### Unity stack trace formats (both appear)

```
Exception style:  QALab.Sandbox.SeededDoor.Open () (at Assets/Sandbox/Scripts/SeededBugs/SeededDoor.cs:27)
Debug.Log style:  QALab.Sandbox.SeededInventory:GetSlot (int) (at Assets/Sandbox/Scripts/SeededBugs/SeededInventory.cs:33)
Engine frame:     UnityEngine.Debug:LogError (object)
IL frame:         System.Collections.Generic.Dictionary`2[TKey,TValue].get_Item (TKey key) (at <b2e5…>:0)
Release build:    QALab.Sandbox.SeededDoor.Open ()          (no "(at file:line)")
```

## labels.json (`qalab.labels/1`): ground truth, eval only

Written by the sandbox only in benchmark mode (`-qalabBenchmark`).

- `seeded_bugs[]`: `bug_id` (SB01–SB16), `type` (`log` | `detector` | `visual`), `feature` (exact H2 heading in `docs/sandbox_design.md`), `expected_severity`, `match` rules, `triggers`.
- `match`: every key given must match an event.
  - `stack_contains`: substring of the stack.
  - `message_regex`: regex search on the message.
  - `detector`: detector name.
  - `near` + `radius`: distance on the ground plane (x, z only) from the event `pos`.
  - `visual_label`: matched against vision findings, not raw events.
- `screenshots[]`: `path` plus `labels` (multi-label, empty = clean frame), plus `bug_ids`.

**Leakage rule:** inference code (`qalab triage`, `qalab vision`) must never read `labels.json`. A pytest test enforces this, for example by blocking `open()` on any `labels.json` while those commands run on the sample run.

## visual_findings.jsonl (`qalab.visual_finding/1`)

One line per analyzed screenshot: `shot`, `method` (`heuristic` | `vlm` | `ml` | `hybrid`), `labels[]` (`label`, `score` 0–1, optional normalized `region`), plus `explanation`, `model`, `latency_ms` and `cached`. An empty `labels` list means the frame looks clean.

Visual label vocabulary (v1): `missing_texture`, `black_screen`, `ui_overflow`, `placeholder_ui`.

## bugs.json (`qalab.bug_report/1`)

The final reports. Key rules:

- `priority` always comes from the ranking formula, never from the LLM.
- `evidence` (≥ 1 item) is a list of `{run_id, seq}` pairs that exist.
- `steps_to_reproduce[].source` is `bot_log`, `inferred` or `template`. `bot_log` steps carry `action_refs`.
- `needs_review` + `review_reasons` record every grounding problem.
- `generator` records method, provider, model, prompt version, latency, tokens, cache hit and attempts.

## LLM structured outputs

- `llm_bug_draft.schema.json`: what the triage model must return.
- `llm_vision.schema.json`: what the vision model must return.

Both are flat: no `$ref`, all properties required, `additionalProperties: false`, so Ollama's `format` field and hosted structured-output modes accept them. The model refers to prompt items by short IDs (`E1`, `A3`, `D2`). Python maps those IDs back to `{run_id, seq}` or a doc chunk id.

## Versioning

- Every file carries `schema: "<name>/<major>"`.
- Adding an optional field: same version.
- Renaming or removing a field, or changing meaning: bump the major (`qalab.event/2`), update both sides, the samples and this page in the same PR, and log it in `docs/DECISIONS.md`.

## Contract tests (both languages)

- `schemas/examples/events_valid.jsonl`: every line must validate (Python), and the C# serializer must produce identical JSON for equivalent objects (Unity EditMode golden test, comparing parsed JSON rather than bytes).
- `schemas/examples/events_invalid.json`: each `event` must fail validation for the reason in `why`.
- Python validates with `jsonschema` (Draft 2020-12). Install `jsonschema[format]` so `date-time` is actually checked, and test that pydantic models accept and reject the same fixtures.
- `samples/sample_run/` must always validate (`qalab validate samples/sample_run`). CI runs this.
