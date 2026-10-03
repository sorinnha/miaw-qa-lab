# Core build progress (foundation + AI core)

Hand-over notes for whoever continues (human or model). Branch: `core-foundation` (pushed; no PR yet).
Scope of this session: spec 02 §1 (loader), §3 (stacks), §7 (context), §8 (LLM report), §9 (providers),
§10 (RAG), plus the pydantic models. Items were built in the order below; each has its own commit.

Run checks: `. .venv/bin/activate && ruff check python && ruff format --check python && pytest python -q`
(98 passed, 2 xfailed = the open YOU WRITE tests).

## Done

1. **Foundation** — `python/pyproject.toml` (spec 02 deps, ruff E/F/I/B/UP @100 cols, pytest),
   `qalab.models` (`Event`, `Run`, `BugReport`, `LLMBugDraft`; `models.schemas` compiles the JSON
   Schemas), `qalab.io.runs` (`load_run`, `iter_events`, `validate_run`, `discover_runs`; invalid lines
   recorded, `suspected_crash`, never opens `labels.json`), `qalab.config` (qalab.toml defaults),
   `qalab validate` CLI. Tests: `tests/models/test_contracts.py`, `tests/io/test_runs.py`.
2. **Providers (§9)** — `qalab.llm`: `LLMProvider`/`LLMResult`/`LLMOutputError`, `FakeProvider`
   (invalid-JSON mode, `overrides`, `script`, hashed embeddings, call recording), `OllamaProvider`
   (`/api/chat` + `format`, `/api/embed`), `LLMCache` + `CachedProvider` (sha256 key, latency/token
   logging), `make_provider()`. Tests: `tests/llm/` (Ollama via `httpx.MockTransport`).
3. **Cluster → context (§3, §4–7)** — `triage.stack` (frame regex, app-frame filter), `triage.cluster`
   (`Cluster`/`ClusterMember`: the shape M2 must produce, D-009), `triage.context.build_context`
   (C/E/A/L/D/CODE, 10k budget, trim E→L→D, `resolve("E2")` → `EventRef`). Fixture clusters from
   `samples/sample_run`: `tests/triage/fixtures.py`.
4. **LLM report (§8)** — `triage.prompts` (loads `prompts/triage_v1.md`, version `triage-v1`),
   `triage.report_template`, `triage.report_llm.generate_report` (≤ 1 + max_retries, error fed back,
   every grounding check → `review_reasons`, template fallback with `needs_review`). Every report
   validates against `bug_report.schema.json`. Tests: `tests/triage/test_report.py`.
5. **RAG (§10)** — `rag.chunk`, `rag.index` (npz cache per file+model, batches of 32),
   `rag.retrieve` (`cosine_top_k` **YOU WRITE stub**, `EmbeddingRetriever`, `TfidfRetriever`,
   `build_query`, `make_retriever`), `rag.code_context`. Tests: `tests/rag/`.

Decisions logged: D-007 … D-011 in `docs/DECISIONS.md`. Module map added to `docs/ARCHITECTURE.md`.

## Next (not started)

- **M2 core:** `triage/normalize.py` (YOU WRITE `normalize_message` + the spec §2 table tests),
  `signature.py`, `cluster.py` variants (union-find, DBSCAN cells), `rank.py` — all producing
  `triage.cluster.Cluster` as documented in D-009.
- **Pipeline + outputs (§11):** `triage/pipeline.py` wiring loader → clusters → `build_context`
  (docs via `rag.make_retriever`, code via `rag.code_context.code_context_for`) → `generate_report`;
  `io/writers.py`, `report/markdown.py`, `jira_csv.py`, `html.py`; CLI `triage run|clusters`,
  `report html`; `triage_meta.json` (sum `generator.*` + `LLMCache.hits`), `validation_report.json`
  (`ValidationReport.to_dict()`).
- **Leakage + smoke tests** from spec 02 (need the pipeline).
- Hosted providers (`gemini`, `openai`, `anthropic`) are stubs in `make_provider` (raise).
- `models/labels.py`, `models/visual.py` (spec 03) not written; `labels.json` is schema-checked by
  `qalab validate` only.
- YOU WRITE open: `rag/retrieve.py::cosine_top_k` (tests xfail until Sora writes it).

## Open questions

- Which embedding model name to default to for Ollama (`make_provider` uses `nomic-embed-text`;
  decide on the PC in M0/ENVIRONMENT.md and move it to `qalab.toml [llm] embed_model`).
- The sample run has one run only, so `runs_affected > 1` and the crash multiplier paths are covered
  by copied fixtures, not real data; M2's rank tests must add the copied-run fixture the spec asks for.
- Branch naming: CLAUDE.md wants one branch per milestone; this session spans M0/M2/M3 pieces, so it
  uses `core-foundation`. Rename or split when opening the PR if preferred.
