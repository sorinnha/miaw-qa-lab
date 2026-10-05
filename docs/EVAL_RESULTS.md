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

Definitions and tuning rules: DECISIONS D-029. Thresholds and the hybrid's N and window are picked on
**val**; every number below is on **test**. Build the dataset from the M5 benchmark first:
```
qalab vision dataset benchmarks\seeded_v1 --out datasets\vision_v1 --seed 7
```
It prints frames and labels per split. Copy any warning about thin test classes into Limitations.

### Hypotheses (from spec 03, adjusted before running)
- H1: heuristics reach recall ≥ 0.95 on `black_screen` and `missing_texture` with ≤ 1 false positive per 100 frames.
- H2: heuristics can't find `ui_overflow` (recall < 0.2); the VLM reaches ≥ 0.6.
- H3: the hybrid keeps ≥ 90% of VLM-only macro recall with ≥ 70% fewer VLM calls.
- H4 (optional): the ML baseline beats heuristics on `placeholder_ui`.

### Results per label
Reproduce:
```
qalab eval vision datasets\vision_v1 --methods heuristic,vlm,ml,hybrid --provider gemini --label v1
```
Fill `cost_per_1k_images` in `qalab.toml [vision]` from the provider's pricing page first, or the cost
column stays n/a. Eval trains its own ML baseline (`qalab vision train-ml` is only for `vision analyze
--method ml`). Paste both tables from `eval\vision_v1.md`: the main one below, and the per-label
false positives (that one decides H1). If it shows a `heuristic (qalab.toml)` row, your configured
thresholds differ from the tuned ones: report both. If it warns about unusable VLM answers, re-run
before trusting the vlm and hybrid rows.

| Method | Split | missing_texture P/R | black_screen P/R | ui_overflow P/R | placeholder_ui P/R | Macro F1 | FP/100 frames | VLM calls | p95 latency ms | Est. cost |
|---|---|---|---|---|---|---|---|---|---|---|
| heuristic | test | | | | | | | 0 | | n/a |
| vlm | test | | | | | | | | | |
| ml | test | | | | | | | 0 | | n/a |
| hybrid | test | | | | | | | | | |

Charts: `eval\vision_v1_prf.png` → `docs/img/vision_prf.png`, `eval\vision_v1_hybrid_tradeoff.png` → `docs/img/hybrid_tradeoff.png`.

### Error analysis
`eval\vision_v1.md` lists 3 false positives and 3 misses, with thumbnails in `eval\vision_v1_errors\`.
Copy the thumbnails to `docs/img/vision_errors/` and replace each `<one-line reason>` after looking at the image.

## Conclusions
- [ ]

## Limitations
- Synthetic sandbox; real-game transfer is checked on a small set only (REAL_GAME.md).
- [ ]
