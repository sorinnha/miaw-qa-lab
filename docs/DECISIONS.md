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

## D-012 · 2026-10-03 · Rank formula kept exactly as spec §6; EXPECTED.md's "SB13 last" is not literally true
- **What:** with the spec weights a single `minor` detector scores 2 × (1 + log2 1) = 2.0 while three warnings score 1 × (1 + log2 3) ≈ 2.58, so on the sample run `perf_spike` (SB08) ends last and SB13 is second to last. Both are P4.
- **Decision:** implement the formula as written and assert the intent (SB13 is in the lowest band and below every error/exception cluster) rather than tune weights to the fixture. Sora can decide on the PC whether `w_det.minor` should be 3 or warnings should carry less than 1; change `qalab.toml`/`W_DET` and EXPECTED.md together.
- **Also:** `rank_clusters` sorts by (priority, −score, signature) so ties are stable across runs.

## D-013 · 2026-10-03 · Pipeline outputs are built from `Cluster` + `BugReport` only; TF-IDF splits identifiers
- **Pipeline:** `triage.pipeline.run_triage` = load → `build_clusters` → `rank_clusters` → per cluster `build_context` (RAG docs + code) → `generate_report` → `write_outputs`. Screenshots are copied to `<out>/shots/<run_id>/<file>` and `attachments` point there (relative to the reports folder, as the schema says). Exit code 3 when any report is P1.
- **Outputs independent of YOU WRITE code:** writers, Markdown/CSV/HTML renderers and `make_reports` are tested on fixture clusters, so only the end-to-end smoke and leakage tests wait on `normalize_message`.
- **TF-IDF fallback:** code identifiers are split before vectorizing (`SeededEnemyRegistry` → `seeded enemy registry`, `fell_out_of_world` → `fell out of world`), which puts the right design-doc heading in the top 3 for every log bug of the sample run. TF-IDF cosines run lower than embedding cosines, so it has its own threshold `[rag] tfidf_min_score = 0.1` (embeddings keep the spec's 0.25).
- **Jira CSV:** description is Jira wiki markup with numbered steps, evidence refs and the QA Lab id/signature so a ticket links back to `bugs.json`.

## D-014 · 2026-10-03 · Events are held in memory per run for now; "last error before crash" is flagged
- **Streaming:** `io.runs.iter_events` streams, but `load_run` keeps each run's validated events in a list because the §7 context needs random access (the 10 actions and 5 logs before a cluster's first occurrence, in any run). This breaks python.md's "never load all events into a list" for the pipeline path. Accepted for M2/M3 with the sample-sized runs; the spec's 100k-event performance check (≤ 10 s, memory ∝ clusters) is deferred to M5, where the fix is a bounded per-run window (last 10 actions / 5 logs captured at each candidate) and clusters that keep only member refs.
- **Crash flag:** spec 00 says triage flags the last errors before an unclean exit. `cluster_facts` now sets `last_before_crash` when a cluster holds the last warning+/detector event of a run without `ended_at`; both the LLM and the template report add the review reason "possible crash", on top of the ×1.25 rank multiplier.
- **Review fixes folded in (fresh-context review of this branch):** `--cluster` is validated (exit 2); a merged log cluster keeps the most severe level; hosted provider names and missing config files exit 2 instead of a traceback; release frames without a file are resolved by class name (`**/{Class}.cs`); the npz cache key includes chunk settings; duplicate `run_id`s across folders are an error; the TF-IDF merge tolerates all-placeholder messages; screenshot paths reach the prompt.

## D-015 · 2026-10-03 · CI smoke step tolerates an open YOU WRITE task (M0)
- **Why:** spec 04 runs `qalab triage run samples/sample_run --provider fake` in CI, but that path calls `normalize_message`, which is Sora's YOU WRITE task. Failing CI on a learning task that is open by design would hide real regressions behind a permanently red badge.
- **How:** `scripts/ci_smoke.py` (Python, so the same file runs on the Windows and Ubuntu runners) validates the sample run, runs spec 04's triage command with the fake provider (no `--docs`, so only `normalize_message` gates it), accepts exit 0 or 3 and checks that freshly written `bugs.json` and `report.html` exist. If the run stops with the traceback line `NotImplementedError: YOU WRITE` it prints a GitHub `::warning::` and exits 0. Any other failure, or a hang past 5 minutes, is red.
- **Consequences:** the first PR after Sora writes `normalize_message` turns the warning into a real smoke test with no CI change. `pytest` stays strict: wrong answers in YOU WRITE tests still fail (xfail only accepts `NotImplementedError`).

## D-016 · 2026-10-03 · CI details beyond spec 04 (M0)
- **Lint scope:** ruff also checks `scripts/` with `--config python/pyproject.toml` (a separate command: with `--config` from the repo root, the package's `src` paths resolve wrongly), so the smoke script and Sora's `hello_events.py` follow the same rules (100 cols, E/F/I/B/UP) as the package. Spec 04 names only `python/`.
- **UTF-8 on Windows:** the job sets `PYTHONUTF8=1`. Windows runners pipe stdout as cp1252, and rich output containing non-ASCII characters would raise `UnicodeEncodeError`. The CLI's own summary line now uses `->` as well.
- **Hygiene:** `permissions: contents: read`, `timeout-minutes: 15`, and a `qalab --help` step that proves the console-script entry point installs on both OSes.
- **hello_events scope:** both `count_events` and `main` are stubs, so the YOU WRITE task is the full ~25-line script PLAN.md describes; the expected output is pinned in its docstring and test.

## D-017 · 2026-10-03 · Seed catalog lives in code, not in a ScriptableObject (M1)
- **What:** spec 01 says LabelRecorder writes labels.json "using the static catalog (a ScriptableObject ...)". The catalog is instead C# (`SandboxSeedCatalog`, engine-free), registered at startup with `LabelRecorder.UseCatalog(...)`; the package only defines `SeedCatalogEntry`/`MatchRule`.
- **Why:** a code catalog is reviewable in a PR, has no asset GUIDs to merge, and runs in `tools/cs-check`, where tests prove every entry equals the matching item of `samples/sample_run/labels.json` (which the Python contract tests validate against the schema) and every `feature` is an H2 heading of `docs/sandbox_design.md`. The YOU WRITE task "SB02 and its catalog entry" becomes a stubbed `SandboxSeedCatalog.SB02()` with a YouWrite-category test.
- **Consequences:** a stubbed entry is skipped (and logged) instead of crashing the catalog, so SB02 is missing from labels.json until Sora writes it. Other games register their own catalog the same way.

## D-018 · 2026-10-03 · cs-check compiles the engine-free code as netstandard2.1, tests run on net8 (M1)
- **Why:** Unity compiles with C# 9 against the .NET Standard 2.1 API. A net8 project alone would accept APIs Unity doesn't have (`ArgumentNullException.ThrowIfNull`, newer string overloads). So `tools/cs-check/Core/QALabCore.csproj` links the engine-free package and sandbox files into a netstandard2.1/C# 9 library with warnings as errors, and `CsCheck.csproj` (net8, NUnit 3) links the EditMode test files and references it.
- **Rules that keep files linkable:** no `UnityEngine`/`UnityEditor` in them (a header comment says so), no records/init accessors (no `IsExternalInit` in Unity 2022), tests find `schemas/` by walking up from the test directory, JSON is compared parsed with numbers by value (`55` = `55.0`).
- **Consequences:** Unity-dependent classes (host, LogCapture, MetricsSampler, RunContext, sandbox MonoBehaviours, scene builder) are compiled only by Unity on Sora's PC; the PR's PC checklist covers them.

## D-019 · 2026-10-03 · Sandbox code is committed before the Unity project exists (M1)
- **Why:** Sora creates `unity/QALabSandbox` from the Universal 3D template in Unity Hub, but the cloud session builds the code first. Unity Hub won't create a project in a non-empty folder, so the PC checklist has Sora create it as `unity/QALabSandbox_new`, copy its files into `unity/QALabSandbox` (keeping `Assets/Sandbox`), and run `scripts/link_sandbox_package.py`, which adds the `file:` dependency and `testables` to the template's manifest without touching anything else.
- **Also:** sandbox scripts get their own asmdefs (`QALab.Sandbox`, `.Editor`, `.Tests.EditMode`) so tests can reference them (Assembly-CSharp can't be referenced). Optional packages are detected with asmdef `versionDefines` (`QALAB_INPUT_SYSTEM`, `QALAB_AI_NAVIGATION`), so the code compiles before AI Navigation is installed and works with either input backend (Unity 6's template enables only the new Input System, where `Input.GetKey` throws).

## D-020 · 2026-10-03 · Seeds reproduce the sample run's stack frames; manual triggers use gameplay paths (M1)
- **Frames:** class and method names follow `samples/sample_run/events.jsonl` (`SeededDoor.Open`, `Interactor.TryInteract`, `HudInventory:Refresh`, `SeededEnemyRegistry.Get` ← `SeededSpawner.AssignTarget`, `FootstepAudio:Play` ← `SandboxPlayer:OnStep`, `MathUtil:SafeDivide` ← `DamageCalculator`/`SpeedModel`), so triage behaves on real runs as it does on the fixture.
- **Real NullReferenceException:** SB01/SB04 dereference plain C# objects, not unassigned serialized fields. In the editor Unity turns a missing serialized reference into UnassignedReference/MissingReferenceException, which would change the message and split clusters between editor and player runs.
- **One path per seed:** the F1 menu, the E key and the M4 bot (`IBotMover.TryInteract`) never call a seed directly; they ask the owning script to act on its next Update (`RequestInteract`, `RequestInteractNearest`, `RequestOpen`, `RequestWave`, `RequestStep`). Any other caller would change the third app frame, and spec 02 §4 signatures use f1–f3, so SB01 would split into two clusters.
- **Seeds and QA Lab:** every seed is on when QA Lab is off; during a run `-qalabSeeds` decides. `LabelRecorder.Trigger` sits next to each misbehaving line and is a no-op outside benchmark mode. No seed id ever appears in log text.
- **M1 limits:** exit code is always 0 (detectors and exit codes 1/2 arrive in M4); the `-qalabAdapter` value is recorded but no bot runs; `QuitOnEnd` leaves Play Mode in the editor.

## D-022 · 2026-10-03 · M1 review follow-ups: run ids, test runs, sandbox geometry (M1)
- **Run id suffix:** two runs started in the same second with the same seed would share `<UTC>-s<seed>`. The second gets `_2` (`_3`, ...) appended; still a valid `run_id` (`^[A-Za-z0-9_.-]+$`), and no run folder is ever overwritten.
- **No auto-start under the Test Runner:** the settings asset's "Auto-start in Play Mode" is ignored when Unity runs with `-runTests` or in the Test Runner's `InitTestScene`; otherwise PlayMode tests would record their deliberate errors as a game run. `QALabSettings.adapter` defaults to `navmesh_explorer` like the flag; the scene builder's asset sets `manual` (no bot in M1).
- **Geometry follows the design doc (the RAG ground truth):** 2 m tiles named T_00–T_399 (index = row × 20 + column), camera 6 m behind and 2.5 m above, respawn at the last checkpoint. Consequence for M4: T_17 is row 0, column 17 (centre x 35 m, z 1 m), so SB06's catalog `near` must come from the builder, not from the hand-made sample run (`[26, 0, 14]`). The NavMesh agent radius (0.3 m in the doc) must be set before SB07 is built.
- **Baked NavMesh is saved as `Assets/Sandbox/Scenes/NavMesh-Sandbox_Level01.asset`**, not embedded in the scene file, so scene diffs stay reviewable.
- **Hot path:** the scene name is cached on scene events only (`Scene.name` allocates on every call); `run_start` is enqueued before log capture starts, so it is always seq 0.
- **Seed ids stay out of all log text,** including QA Lab's own `[QALab]` warnings (they reach `player.log`, which lives in the run folder): those warnings report counts only.
