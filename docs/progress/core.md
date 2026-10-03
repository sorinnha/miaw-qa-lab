# Core engine progress: hand-off note

Branch `core-foundation`. PR #1 (models, LLM layer, context, RAG) is merged into `main`; the follow-up PR
adds the M2 core, the pipeline and outputs, and the review fixes. Spec 02 §1–§12 are built except where
noted under **Open**. Hosted providers are not built (optional extras).

Run checks: `. .venv/bin/activate && ruff check python && ruff format --check python && pytest python -q`
→ `134 passed, 48 xfailed`. The 48 xfails are the YOU WRITE tests (see below); with a temporary
implementation of both stubs the same run is `134 passed, 48 xpassed`.

## Done (by module)

- `qalab.models` — `Event`, `Run`, `BugReport`, `LLMBugDraft`, `Labels`, `VisualFinding`; `models.schemas`
  compiles `schemas/*.json`. Contract tests prove schema and model accept/reject the same fixtures.
- `qalab.io.runs` — load/validate/stream runs, invalid lines recorded, `suspected_crash`; never opens
  `labels.json`. `qalab.io.writers` — bugs.json, clusters.json, screenshot copies.
- `qalab.config` + `qalab.toml` — spec §12 defaults plus `[rag] tfidf_min_score`.
- `qalab.llm` — `LLMProvider`/`LLMResult`, `FakeProvider`, `OllamaProvider`, `LLMCache` + `CachedProvider`,
  `make_provider` (`none` → None; hosted names → `ValueError`).
- `qalab.triage` — `normalize` (**YOU WRITE stub**), `stack`, `signature`, `cluster` (`Cluster` type,
  exact + DBSCAN cells + variants with union-find), `rank`, `context`, `prompts`, `report_template`,
  `report_llm` (retries, grounding, crash flag), `pipeline` (`run_triage`).
- `qalab.rag` — `chunk`, `index` (npz cache per file/model/chunk settings), `retrieve`
  (`cosine_top_k` **YOU WRITE stub**, `EmbeddingRetriever`, `TfidfRetriever` with identifier splitting),
  `code_context`.
- `qalab.report` — `markdown`, `jira_csv`, `html` (self-contained Jinja2 page, filter, expand, thumbnails).
- `qalab.cli` — `validate`, `triage run` (exit 0 / 3 on P1 / 2 on error), `triage clusters`, `report html`.
- Sample-run facts (verified with a temporary normalize): exact = 9 clusters, frame_tfidf = 8, tfidf_only
  merges SB01+SB04; ranking matches D-012.

Decisions: D-007 … D-014 in `docs/DECISIONS.md`. Module map in `docs/ARCHITECTURE.md`.

## Open YOU WRITE tasks (Sora)

| File | Function | Tests |
|---|---|---|
| `python/src/qalab/triage/normalize.py` | `normalize_message` | `pytest python/tests/triage/test_normalize.py -m youwrite -rxX` (+ cluster, signature, CLI smoke, leakage, config-reaches-pipeline tests) |
| `python/src/qalab/rag/retrieve.py` | `cosine_top_k` | `pytest python/tests/rag/test_retrieve.py -m youwrite -rxX` |

Until `normalize_message` exists, `qalab triage run` raises `NotImplementedError("YOU WRITE")`.

## Next sessions

- **M2 close-out (PC):** Sora writes `normalize_message`; `/review-mine` drops the markers; run
  `pytest python -q --cov=qalab.triage --cov-report=term-missing` and record ≥ 80% on normalize/stack/signature
  in PLAN.md; tick M2.
- **M3 close-out (PC):** Sora writes `cosine_top_k`; a real Ollama run on the sample with
  `--docs docs/sandbox_design.md`; check evidence resolves and steps cite actions; tag v0.1.0 (ask first).
- **Not built / deferred:** hosted providers (`llm/gemini.py`, `openai_.py`, `anthropic_.py`); streaming
  pipeline for 100k events (D-014); `qalab eval` (M5); README demo GIF and measured numbers (M5/M7).
  CI (`.github/workflows/python-ci.yml`, with `--cov`) was added on the M0 branch.

## Open questions

- Rank weights vs EXPECTED.md "SB13 last" (D-012): keep the spec formula or raise `minor` to 3?
- Default Ollama embed model name (`nomic-embed-text` in `make_provider`): confirm on the PC and move to
  `qalab.toml [llm] embed_model`.
- Branch naming: CLAUDE.md wants `m<N>-<slug>`; this work spans M0/M2/M3, hence `core-foundation`.
