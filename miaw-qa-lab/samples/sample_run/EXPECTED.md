# Sample run: expected results

A hand-made 55-second run of the sandbox (seed 42) used as a test fixture and README demo. Every file here validates against `schemas/`. The screenshots are tiny synthetic 160x90 frames.

`labels.json` is ground truth for `qalab eval` only. Triage and vision code must never read it.

## Events (38 lines)

| Bug | Events (seq) | What makes it interesting |
|---|---|---|
| SB01 Doors | 8 | NullReferenceException; same message as SB04 |
| SB02 Inventory | 11, 15 | `Debug.LogError` with different numbers (7 vs 9), `Class:Method` stack format |
| SB03 Enemy registry | 19, 26 | KeyNotFoundException with different hex ids in quotes; first frame is `System.*` |
| SB04 Spawner | 18 | Same message as SB01, different stack, so it must stay a separate cluster |
| SB06 Level geometry | 23 | `fell_out_of_world` detector; has a screenshot |
| SB08 Performance | 21 | `perf_spike` detector |
| SB13 Audio | 13, 16, 28 | Warning spam with different quoted surface names; should rank lowest |
| SB14 Combat math | 30, 31 | Same root cause (`MathUtil:SafeDivide`) but different message text |

Not a bug: seq 34 (`info` checkpoint log). It's context only, never a cluster.

## Expected triage output

- **exact** signatures: **9 clusters** for 8 bugs (SB14 splits into 2).
- **frame_tfidf / frame_embed**: **8 clusters**, all correct. SB14 merges because both events share the top app frame `QALab.Sandbox.MathUtil.SafeDivide` (TF-IDF cosine of the two messages is about 0.69).
- **tfidf_only**: also **8 clusters**, but wrong. SB01 and SB04 merge (identical messages, cosine 1.0) and SB14 stays split. The cluster count alone hides this; pairwise precision/recall shows it.
- In the frame-aware variants SB01 and SB04 must **never** merge.
- `MiawWorks.QALab.*` frames (the bot) must not be part of any signature.
- Ranking: SB13 (warning spam) is last.
- Every report cites evidence that exists in `events.jsonl`.

## Screenshots

| File | Labels | Note |
|---|---|---|
| shots/000001.png | none | clean |
| shots/000002.png | none | clean |
| shots/000003.png | none | taken by the fall detector (sky view) |
| shots/000004.png | missing_texture (SB09) | about 13% magenta pixels |
| shots/000005.png | black_screen (SB10) | about 96% near-black, HUD still visible |

Expected vision results: heuristics flag 000004 as `missing_texture` and 000005 as `black_screen`, and nothing else.
