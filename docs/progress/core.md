# Core build progress (foundation + AI core)

Hand-over notes for whoever continues (human or model). Branch: `core-foundation`.
Scope of this session: spec 02 items §1 (loader), §7 (context), §8 (LLM report), §9 (providers), §10 (RAG),
plus the pydantic models. Clustering/ranking (§2–§6) and outputs (§11) belong to M2 and are NOT built here.

## Done

- Repo layout fixed (nested folder moved to the root).
- `python/pyproject.toml`: spec 02 dependencies, ruff (E F I B UP, 100 cols), pytest.
- `qalab.models`: `Event`, `Run`, `BugReport`, `LLMBugDraft` (+ payload models) mirroring `schemas/`.
  `qalab.models.schemas` compiles the JSON Schemas (Draft 2020-12, format checked).
- `qalab.io.runs`: discover runs, stream + validate events, skip and record bad lines, sort by seq,
  `suspected_crash` when `ended_at` is missing. Never opens `labels.json`.
- `qalab.config`: `qalab.toml` loader with spec §12 defaults.
- `qalab.cli`: `qalab validate <run_dir>...`.
- Tests: `tests/models/test_contracts.py`, `tests/io/test_runs.py`.

- `qalab.llm`: `LLMProvider` protocol + `LLMResult`; `FakeProvider` (invalid-JSON mode, overrides,
  script, hashed bag-of-words embeddings); `OllamaProvider` (`/api/chat` with `format` schema,
  `/api/embed`); `LLMCache` (SQLite) + `CachedProvider` (cache + latency/token logging);
  `make_provider()`. Tests: `tests/llm/`.

## Next

- Item 3: typed `Cluster` (spec §4–6 shape) and the §7 context builder.

## Open questions

- None yet.
