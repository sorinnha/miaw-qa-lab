# Decisions

One entry per real choice: what we decided, why, what else we considered, and what it costs. Newest at the bottom.

## D-001 · 2026-10-03 · Monorepo: Unity package + Python CLI + shared schemas
- **Why:** one pipeline, one README, one CI; the schemas sit next to both sides that use them.
- **Alternatives:** separate repos per module (more pinned repos, but duplicated contracts and harder end-to-end demos).
- **Consequences:** games install the package by git URL with `?path=/unity/com.miawworks.qalab`.

## D-002 · 2026-10-03 · Local model (Ollama) as the default provider; providers stay pluggable
- **Why:** free, works offline, and unreleased game data stays on the machine. Some hosted free tiers may use submitted content to improve their products.
- **Alternatives:** hosted APIs (often better quality, but cost and data-handling trade-offs).
- **Consequences:** smaller models need stricter validation and grounding. E2 measures the quality gap.

## D-003 · 2026-10-03 · Seeded-bug benchmark with labels from engine state
- **Why:** precision and recall without manual labelling; repeatable across seeds.
- **Alternatives:** hand-labelled runs only (slow, small).
- **Consequences:** labels must never reach inference code (enforced by a test). We also need a small real-game set (M7) to check the results transfer.

## D-004 · 2026-10-03 · Priority is computed, severity is suggested
- **Why:** QC leads need an explainable, stable ordering; LLM output can vary.
- **Consequences:** a severity/priority disagreement greater than one level sets `needs_review`.

## D-005 · 2026-10-03 · Playtests run as a windowed development player
- **Why:** that's what QC tests; full-fidelity screenshots including overlay UI.
- **Alternatives:** editor batch-mode PlayMode tests (great for CI of the framework, but no overlay UI in captures).
- **Consequences:** the build step is part of the pipeline. Confirm with the M4 screenshot spike.

## D-006 · 2026-10-03 · Code can be built in cloud sessions; engine-free C# is tested outside Unity
- **Why:** a claude.ai/code cloud session (Linux, no Unity or Ollama) can build and test most of the code while Sora's PC stays free. What it can't check is listed, not hidden.
- **How:** one milestone per cloud session and draft PR (CLAUDE.md → Cloud mode). Unity, Ollama and PowerShell steps go in the PR's PC checklist. Engine-free C# is compiled and unit-tested with .NET 8 in `tools/cs-check`, also as a CI job. YOU WRITE tests are marked `youwrite` and stay xfail until Sora writes the function.
- **Alternatives:** build everything locally (slower, and the PC is busy); let the cloud write the YOU WRITE solutions (defeats the point of the project).
- **Consequences:** the pipeline can't run end to end until Sora finishes the YOU WRITE tasks it depends on. Code that calls Unity APIs is verified only on the PC.

## D-007 · 2026-10-03 · Python validates raw JSON against `schemas/` first, then builds pydantic models
- **Why:** the schema files are the contract; pydantic models are a typed convenience. Checking the schema first gives error messages that name the schema rule, and the contract test proves both reject the same fixtures.
- **How:** `qalab.models.schemas` resolves `schemas/` relative to the source tree (`QALAB_SCHEMAS_DIR` overrides it for installs outside the repo). `Event.ts` stays a string: jsonschema already checks `date-time`, and triage only uses `t`.
- **Consequences:** two validation passes per line (cheap; the sample run parses in milliseconds). Changing a schema still requires touching the matching model.

## D-008 · 2026-10-03 · Provider layer: `CachedProvider` wraps every real provider; bad output is an exception
- **Why:** caching and latency/token logging must behave the same for Ollama, Fake and future hosted providers, so they live in one wrapper instead of in each client. A non-JSON answer raises `LLMOutputError` (carrying the raw text) so the report layer can retry with the error message, same as a pydantic validation failure.
- **How:** `make_provider()` returns `CachedProvider(inner, LLMCache | None, prompt_version)`; `--no-cache` passes `cache=None`. The cache key is sha256 over provider, model, prompt_version, system, user, schema and image hashes. `--provider none` returns `None` (template reports, TF-IDF retrieval).
- **Alternatives:** caching inside each provider (duplicated code); returning an empty result on bad output (hides failures).
- **Consequences:** hosted providers (`gemini`, `openai`, `anthropic`) raise `NotImplementedError` until they are written; tests use `FakeProvider` and `httpx.MockTransport` only.

## D-009 · 2026-10-03 · `Cluster` is the contract between M2 and the report side; context is trimmed by item size
- **Why:** the LLM/RAG side was built before clustering, so `qalab.triage.cluster.Cluster` fixes what M2 must produce: sorted members (`(run_id, seq)`), parsed app frames per member, `level`/`exception_type`/`normalized_message` for logs, `detector`/`detector_severity`/`cell` for detectors, and `score`/`priority`/`score_breakdown` from rank.py. `stack.py` (§3) was built early because §7 and §10 need app frames.
- **How:** "first occurrence" = the member with the lowest `(run_id, seq)`; its run supplies the A and L lists. E picks first, last, then a greedy max-min over token Jaccard distance of raw messages (deterministic, ties by lower seq). The 10,000-char budget is measured on the context items (message, frames, doc text, code, facts JSON), not on the rendered prompt, so the template's fixed text doesn't count; trim order is E (keeping E1), then L, then D. A and CODE are never trimmed.
- **Alternatives:** measuring the rendered prompt (couples the budget to template wording); TF-IDF for "most different" messages (heavier, same result on short log lines).
- **Consequences:** M2 tests must build clusters through this type (see `tests/triage/fixtures.py` for the shape).

## D-010 · 2026-10-03 · LLM report: retry only on bad output, fall back on transport errors, never trust ids
- **Why:** feeding "your previous output failed validation: <error>" back fixes format mistakes, but cannot fix an unreachable server, so an `LLMError` falls straight back to the template (one attempt, reason recorded). Every id the model cites is checked against the prompt's E/A/D sets; nothing in `bugs.json` points at an event or chunk the model did not see.
- **How:** `request_draft()` runs ≤ 1 + `max_retries` attempts and sums tokens/latency across them; `ground_draft()` implements the §8 checks (unknown E ids dropped → E1 if none left; `bot_log` steps with unknown or empty A ids become `inferred`; unknown D ids dropped; severity vs priority gap > 1; confidence < 0.5), each adding one `review_reasons` line. Template severity mirrors the computed priority (S1↔P1). Titles are cut to the schema's 100 chars.
- **Consequences:** `generator.attempts` counts failed calls too (3 when two answers were broken); the fallback report keeps provider/model/attempts so the cost is visible.

## D-011 · 2026-10-03 · RAG: heading-first chunks, per-file npz cache, TF-IDF when there is no provider
- **Why:** design-doc sections are short and named after features, so the heading is the strongest signal; each chunk is embedded as `"{heading}\n{text}"`. Caching per file (key = sha256 of model + file contents) means editing one doc re-embeds only that doc. Without an embedding model (`--provider none`) a TF-IDF retriever over the same chunks keeps reports grounded in docs, and gives E2 a lexical baseline.
- **How:** `rag.chunk` splits on H1–H3, then ~800 chars at sentence ends with ~100 overlap; ids are `{file}#{slug}-{n}`. `rag.index.build_index` embeds in batches of 32 to `.cache/index/<key>.npz` + `.json`. `rag.retrieve.cosine_top_k` is Sora's YOU WRITE task (its tests and the `EmbeddingRetriever` tests are marked `youwrite`); `TfidfRetriever` uses scikit-learn's L2-normalized rows. `rag.code_context` resolves `Assets/...cs` under `--repo` (root first, then up to three folder levels down), falls back to `class X` + `Method(` lookup when there's no line, and returns ±8 numbered lines capped at 40.
- **Consequences:** the retrieval query mixes normalized message, top frame, scene and detector, so detector-only clusters retrieve mostly by scene and detector name; hit@3 for those is measured in M5, not assumed.
