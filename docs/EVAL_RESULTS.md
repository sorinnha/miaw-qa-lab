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

Metric definitions: DECISIONS D-028. Every command writes `eval\triage_<label>.json` (all numbers),
`eval\triage_<label>.md` (these tables, ready to paste) and charts. Paste the tables from the `.md`
file; don't retype numbers. Record the benchmark first: `scripts\benchmark.ps1 -Seeds (1..20) -Duration 120`.

### Hypotheses (write before running)
- E1: `frame_tfidf` beats `exact` on pairwise F1 (it fixes the SB14 split) without merging SB01/SB04. `tfidf_only` has lower precision.
- E2: [ ]
- E3: [ ]

### E1: clustering variants
Reproduce: `qalab eval triage benchmarks\seeded_v1 --variants exact,frame_tfidf,frame_embed,tfidf_only --provider gemini --label e1`
(`frame_embed` needs an embedding model: Gemini's here, D-021. With `--provider none`, or if the
provider is unreachable, it is listed as skipped and the other variants are still written.)

| Variant | Pairwise P | Pairwise R | F1 | Clusters / true bugs | Notes |
|---|---|---|---|---|---|
| exact | | | | | |
| frame_tfidf | | | | | |
| frame_embed | | | | | |
| tfidf_only | | | | | |

Chart: `eval\triage_e1_e1_prf.png` → copy to `docs/img/triage_e1_prf.png`.

### E2: models (report quality)
Choose the two models first and write them in the hypothesis. The plan says "a small local model vs a
larger or hosted one". This PC has no Ollama (D-021), so either install Ollama for the local side, or
compare two hosted sizes (for example a "lite" and a full Gemini model) and say so. Reproduce, once
per model (same benchmark, same prompt, RAG on):
```
qalab eval triage benchmarks\seeded_v1 --variants none --reports --docs docs\sandbox_design.md --provider <provider A> --model <model A> --label e2-a
qalab eval triage benchmarks\seeded_v1 --variants none --reports --docs docs\sandbox_design.md --provider <provider B> --model <model B> --label e2-b
```

| Setting | Fallback rate | Field completeness | Grounding rate | Repro-step match | Severity agreement (±1) | Component correct | Retrieval hit@3 | Latency p50 / p95 ms | Tokens per report |
|---|---|---|---|---|---|---|---|---|---|
| | | | | | | | | | |

Chart: `eval\triage_e2-b_reports.png` → `docs/img/triage_reports.png`. Latency comes only from uncached calls: add `--no-cache` for a fair timing run.

### E3: RAG on vs off
Reproduce (same model; `--design-doc` scores "component correct" without giving the doc to the model):
```
qalab eval triage benchmarks\seeded_v1 --variants none --reports --provider gemini --design-doc docs\sandbox_design.md --label e3-rag-off
qalab eval triage benchmarks\seeded_v1 --variants none --reports --provider gemini --docs docs\sandbox_design.md --label e3-rag-on
```

| Setting | Fallback rate | Field completeness | Grounding rate | Repro-step match | Severity agreement (±1) | Component correct | Retrieval hit@3 | Latency p50 / p95 ms | Tokens per report |
|---|---|---|---|---|---|---|---|---|---|
| e3-rag-off | | | | | | | n/a | | |
| e3-rag-on | | | | | | | | | |

Manual check (not automated, D-028): read 10 reports from each of `eval\reports_e3-rag-off\report.md`
and `eval\reports_e3-rag-on\report.md`. Count how many have an "expected" that matches the design doc: rag-off __/10, rag-on __/10.

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
