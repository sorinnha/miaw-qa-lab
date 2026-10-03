# Spec 03: `qalab vision`, visual glitch detection with an honest evaluation

**Milestone:** M6 (dataset, heuristics, VLM, optional ML baseline, hybrid, eval, triage integration).

## Goal

Flag rendering and UI bugs in playtest screenshots, then **measure** which method works: cheap heuristics, a vision-language model (VLM), a small classic-ML model, or a hybrid. The write-up of the experiments matters as much as the code. It mirrors how an R&D team validates a new technique before QC teams use it.

## Labels (v1)

| Label | What it looks like | Seed |
|---|---|---|
| `missing_texture` | Magenta/pink surfaces (Unity's missing material or shader color) | SB09 |
| `black_screen` | The 3D view is (almost) fully black; HUD may still draw | SB10 |
| `ui_overflow` | Text spilling out of its box, clipped, or overlapping | SB11 |
| `placeholder_ui` | Plain white box where an icon should be | SB12 |

## CLI

```
qalab vision analyze <run_dir>... [--method heuristic|vlm|ml|hybrid] [--provider ...] [--model ...]
       → <run_dir>/visual_findings.jsonl
qalab vision dataset <run_dir|glob>... --out datasets/vision_v1 [--seed 7]
       → index.csv (path, run_id, t, scene, labels "a|b", split)
qalab vision train-ml <dataset_dir>                      (optional baseline)
qalab eval vision <dataset_dir> [--methods heuristic,vlm,ml,hybrid] --out eval/
```

## Dataset builder

- Sources: screenshots plus `labels.json` from benchmark runs (`scripts/benchmark.ps1`, e.g. 20 seeds × 120 s). Labels come from engine state (spec 01), so no manual labelling.
- **Split by `run_id`** (70/15/15, seeded), never by frame. Frames from one run are near-duplicates, so a frame split leaks and inflates scores. A test asserts no run_id appears in two splits.
- Report class counts per split. If a class has fewer than 10 test frames, say so in EVAL_RESULTS (results are noisy).
- Add 20–40 real frames from Sora's game (M7), hand-labelled, as a separate "real-world" test set.

## Methods

### Heuristics (`vision/heuristics.py`)

Work on RGB downscaled to a long side of 640. Each returns `(score 0–1, stat)`. Thresholds are tuned on the **val** split only.

| Label | Statistic | Starting threshold |
|---|---|---|
| `black_screen` | share of pixels with luminance (0.299R + 0.587G + 0.114B) < 16 | ≥ 0.90 (robust to HUD overlays) |
| `missing_texture` | `magenta_ratio`: share of pixels with R > 180, G < 80, B > 180, \|R−B\| < 60 (**YOU WRITE**) | ≥ 0.003 |
| `placeholder_ui` | OpenCV connected components of near-white pixels (all channels ≥ 245): area ≥ 0.2% of the frame, fill ratio (area / bbox area) ≥ 0.9, aspect between 0.2 and 5 | any component |
| `ui_overflow` | not attempted; documented as a known gap | n/a |

- Score mapping: `min(1, stat / threshold)` above the threshold, else a scaled-down value. Keep it simple and monotonic.
- Expected trap: post-processing (bloom, tonemapping in URP) shifts the magenta color. Check on real frames and record what you find.

### VLM (`vision/vlm.py`)

- Prompt `prompts/vision_v1.md`. Structured output = `schemas/llm_vision.schema.json`, temperature 0.
- Downscale to a long side of 768 and send as PNG/JPEG bytes via `LLMProvider.complete_json(..., images=[...])`.
- Cache key = sha256(image bytes) + model + prompt version.
- Record latency per image and tokens if the provider reports them.

### Classic ML baseline (optional, `vision/ml.py`)

- Features: 3×16-bin HSV histogram, edge density (Canny), mean/std luminance, share of near-white and near-black pixels.
- One-vs-rest logistic regression (scikit-learn, `class_weight="balanced"`). Thresholds per label tuned on val; report on test.
- Point: show train/val/test discipline, overfitting checks, and when a simple model is enough.

### Hybrid (`vision/hybrid.py`)

- Heuristics handle `black_screen`, `missing_texture` and `placeholder_ui`.
- Call the VLM only when (a) no heuristic fired AND the frame is within ±2 s of a UI action or detector event, or (b) it's every Nth remaining frame (N = 5).
- Tune N and the window on val. Report the VLM calls saved against recall lost.

## Evaluation (`qalab eval vision`)

- Per label: precision, recall, F1 (multi-label; a frame can have several labels). Also macro-F1 and false positives per 100 frames.
- Cost and speed per method: latency p50/p95, VLM calls, estimated cost. For hosted models, `cost_per_1k_images` comes from `qalab.toml`; Sora fills it in from the provider's pricing page. Don't invent it.
- Charts saved as PNG with matplotlib:
  - P/R/F1 per label per method (grouped bars);
  - macro recall against VLM calls (hybrid trade-off).
- Save raw results to `eval/vision_<date>.json` and summarize in `docs/EVAL_RESULTS.md`. Include 3 false positives and 3 misses as thumbnails with one-line explanations.

### Hypotheses: write these in EVAL_RESULTS.md *before* running

- **H1:** heuristics reach recall ≥ 0.95 on `black_screen` and `missing_texture` with ≤ 1 false positive per 100 frames.
- **H2:** heuristics can't find `ui_overflow` (recall < 0.2); the VLM reaches ≥ 0.6.
- **H3:** the hybrid keeps ≥ 90% of VLM-only macro recall with ≥ 70% fewer VLM calls.
- **H4 (optional):** the ML baseline beats heuristics on `placeholder_ui`.

Report honestly whether each held. Failed hypotheses with a good explanation are great interview material.

## Integration with triage

- The triage loader reads `visual_findings.jsonl` when it exists (never edit `events.jsonl`). Each label with score ≥ 0.5 becomes an in-memory `visual:<label>` detector event. Its evidence is the matching `screenshot` event (same `data.path`), so every evidence ref stays a real `{run_id, seq}` from `events.jsonl`.
- Severity map: `black_screen` major, `missing_texture` minor, `ui_overflow` minor, `placeholder_ui` minor.
- These events cluster like detectors (label + scene + 4 m cell + DBSCAN). The report attaches the best-scoring screenshot.

## Tests

- Synthetic images made with numpy in tests for each heuristic: pure black, black + small HUD, magenta patch of 0.5% and 0.1%, white rectangle versus white noise, a clean gradient.
- `samples/sample_run/shots`: 000004 → `missing_texture`, 000005 → `black_screen`, others clean (EXPECTED.md).
- VLM path with FakeProvider (returns labels from a lookup table keyed by filename). Cache hit on the second call.
- Dataset split: no run_id in two splits; the same seed gives the same split.
- Metrics: hand-checked toy example (3 frames, 2 labels).
- Leakage: `qalab vision analyze` never opens `labels.json` (shared test with triage).

## Acceptance (M6)

`qalab eval vision datasets/vision_v1 --methods heuristic,vlm,hybrid` runs end to end. EVAL_RESULTS.md has the hypotheses, tables, two charts, error examples, conclusions and limitations. Visual bugs appear in `report.html` with screenshots.
