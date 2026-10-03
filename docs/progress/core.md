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

- `qalab.triage.stack`: spec §3 frame regex, `Frame`, app-frame filter (built early: §7/§10 need it).
- `qalab.triage.cluster`: `Cluster` + `ClusterMember`, the shape M2 must produce (D-009).
- `qalab.triage.context`: `build_context()` → `ClusterContext` (C/E/A/L/D/CODE, 10k-char budget,
  trim E→L→D, `resolve("E2")` → `EventRef`, `to_prompt_vars()` for the Jinja prompt).
  Fixture clusters: `tests/triage/fixtures.py`.

## Next

- Item 4: §8 `report_llm.py` + `report_template.py` (prompt, retries, grounding checks).

## Open questions

- None yet.
