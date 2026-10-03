# Evaluation results

Every number here must be reproducible with the command in its section. Write the hypotheses before running anything.

## Setup

| Item | Value |
|---|---|
| Benchmark | `benchmarks/seeded_v1` · seeds 1–20 · 120 s each · sandbox build [version / git sha] |
| Machine | [from ENVIRONMENT.md] |
| Models | text [ ] · embeddings [ ] · vision [ ] |
| Date | [ ] |

## Triage (M5)

### Hypotheses (write before running)
- E1: `frame_tfidf` beats `exact` on pairwise F1 (it fixes the SB14 split) without merging SB01/SB04. `tfidf_only` has lower precision.
- E2: [ ]
- E3: [ ]

### E1: clustering variants
Reproduce: `qalab eval triage benchmarks\seeded_v1 --variants exact,frame_tfidf,frame_embed,tfidf_only`

| Variant | Pairwise P | Pairwise R | F1 | Clusters / true bugs | Notes |
|---|---|---|---|---|---|
| exact | | | | | |
| frame_tfidf | | | | | |
| frame_embed | | | | | |
| tfidf_only | | | | | |

### E2: models (report quality)

| Model | Field completeness | Grounding rate | Repro-step match | Severity agreement | Latency p50 / p95 | Tokens per report |
|---|---|---|---|---|---|---|
| | | | | | | |

### E3: RAG on vs off

| Setting | Retrieval hit@3 | "expected" filled correctly | Component correct | Notes |
|---|---|---|---|---|
| RAG off | n/a | | | |
| RAG on | | | | |

## Vision (M6)

### Hypotheses (from spec 03, adjusted before running)
- H1 · H2 · H3 · H4

### Results per label

| Method | missing_texture P/R | black_screen P/R | ui_overflow P/R | placeholder_ui P/R | Macro F1 | FP/100 frames | VLM calls | p95 latency |
|---|---|---|---|---|---|---|---|---|
| heuristic | | | | | | | 0 | |
| vlm | | | | | | | | |
| ml (optional) | | | | | | | 0 | |
| hybrid | | | | | | | | |

Charts: `docs/img/vision_prf.png`, `docs/img/hybrid_tradeoff.png`

### Error analysis
3 false positives and 3 misses with thumbnails and one-line reasons.

## Conclusions
- [ ]

## Limitations
- Synthetic sandbox; real-game transfer is checked on a small set only (REAL_GAME.md).
- [ ]
