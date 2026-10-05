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

## D-021 · 2026-10-03 · Gemini is the configured provider; Ollama stays supported (M3)
- **Why:** Sora's PC has no Ollama, so the local default from D-002 can't run there. Google's Gemini API has a free tier, structured JSON output with a JSON Schema, image input and an embedding model, which covers triage (§8), RAG (§10) and later vision (spec 03) with one key.
- **How:** `qalab.llm.gemini.GeminiProvider` on the official `google-genai` SDK (optional `[gemini]` extra): `generate_content` with `system_instruction`, `temperature` (0 by default), `response_mime_type="application/json"` and `response_json_schema` (the draft schema minus `$schema`/`$id`/`title`/`description`), screenshots as `inline_data` parts; `embed_content` with `gemini-embedding-001`, rows L2-normalized. Requests are plain dicts, so the SDK is imported only when a real client is built; tests use a fake client and also validate the dicts against the SDK's own pydantic types when it is installed. Blocked/empty/non-JSON answers raise `LLMOutputError` (the report layer retries); SDK/HTTP failures raise `LLMError` with the HTTP code only, never the prompt or the key. Tokens out = answer + thinking tokens (both billed).
- **Config:** `qalab.toml [llm] provider = "gemini"`, `model = "gemini-2.5-flash"`, `embed_model = "gemini-embedding-001"`; precedence `--provider` > `QALAB_PROVIDER` > `qalab.toml`, and `--model` > `QALAB_MODEL` > `qalab.toml`. The toml's model names apply only to the toml's provider, so `--provider ollama` doesn't receive a Gemini model name. The CLI loads `.env` (stdlib parser, shell variables win, values never logged); pytest sets `QALAB_NO_DOTENV` and clears key variables so tests can never use a real key.
- **Data:** free-tier inputs may be used by Google to improve its products, so only sandbox data is sent (README → Data handling). Without `GEMINI_API_KEY` the CLI exits 2 before any data leaves the machine.
- **Alternatives:** keep Ollama as the configured default (doesn't run on the PC); OpenAI/Anthropic SDKs (paid only for this use); a raw REST client (more code to test; the spec asks for each SDK's structured-output feature).
- **Consequences:** model names change over time; if `gemini-2.5-flash` is retired, set `[llm] model` in `qalab.toml` (or `QALAB_MODEL`). Decision number D-021 because the open M1 branch uses D-017–D-020.

## D-022 · 2026-10-03 · M1 review follow-ups: run ids, test runs, sandbox geometry (M1)
- **Run id suffix:** two runs started in the same second with the same seed would share `<UTC>-s<seed>`. The second gets `_2` (`_3`, ...) appended; still a valid `run_id` (`^[A-Za-z0-9_.-]+$`), and no run folder is ever overwritten.
- **No auto-start under the Test Runner:** the settings asset's "Auto-start in Play Mode" is ignored when Unity runs with `-runTests` or in the Test Runner's `InitTestScene`; otherwise PlayMode tests would record their deliberate errors as a game run. `QALabSettings.adapter` defaults to `navmesh_explorer` like the flag; the scene builder's asset sets `manual` (no bot in M1).
- **Geometry follows the design doc (the RAG ground truth):** 2 m tiles named T_00–T_399 (index = row × 20 + column), camera 6 m behind and 2.5 m above, respawn at the last checkpoint. Consequence for M4: T_17 is row 0, column 17 (centre x 35 m, z 1 m), so SB06's catalog `near` must come from the builder, not from the hand-made sample run (`[26, 0, 14]`). The NavMesh agent radius (0.3 m in the doc) must be set before SB07 is built.
- **Baked NavMesh is saved as `Assets/Sandbox/Scenes/NavMesh-Sandbox_Level01.asset`**, not embedded in the scene file, so scene diffs stay reviewable.
- **Hot path:** the scene name is cached on scene events only (`Scene.name` allocates on every call); `run_start` is enqueued before log capture starts, so it is always seq 0.
- **Seed ids stay out of all log text,** including QA Lab's own `[QALab]` warnings (they reach `player.log`, which lives in the run folder): those warnings report counts only.

## D-023 · 2026-10-04 · M1 re-review follow-ups: disjoint seed rules, a clean run end (M1)
- **Seed match rules must be disjoint on real stacks.** SB03's KeyNotFoundException was thrown under `SeededSpawner.SpawnWave`, so it also matched SB04's `stack_contains: "SeededSpawner.SpawnWave"`, and one exception counted as two seeded bugs. `Update` now assigns targets after `SpawnWave` returns (real SB03 stack: `Get ← AssignTarget ← Update`); the catalog and the sample run are unchanged. cs-check checks every catalog rule against one real-run event per seed (each must match only its own seed); a sandbox PlayMode test checks the real SB03/SB04 stacks from `Update` against both rules. New log seeds (SB15 in M4) get a row in the same test.
- **Run end:** `EventWriter.Close("run_end", ...)` stops accepting events (later calls return null and take no seq), waits for producers already in flight (an `Interlocked` counter; they never block, the wait is capped at 1 s for a thread frozen in a debugger), writes `run_end` with the last seq, drains and closes. So events.jsonl has no seq gaps and nothing after `run_end`, even when another thread logs while the run ends; the exceptions are a producer frozen past the 1 s cap (its event is lost, a gap) and logs that start after the end (dropped, by design). The host stops log capture, then calls `Close`.
- **`seeds_enabled`:** `-qalabSeeds all` records every catalog id; a list records exactly the listed ids, including ones the catalog doesn't have yet (SB02 until its YOU WRITE entry exists, M4 seeds). The rule lives in the engine-free `SeedSelection.EnabledIds` and is tested in cs-check.
- **`run.json`** replaces an existing file with `File.Replace` (one call) instead of delete + move; if the OS swap fails halfway, the new content is still in `run.json.tmp`.
- **`unity_tests.ps1`** waits for the Unity process only (`WaitForExit`), not `Start-Process -Wait`, which also waits for child processes such as Unity's licensing client.
- **PC setup:** the project is created as `QALabSandbox_new` and its ProjectSettings are copied, so Player Settings → Product Name must be set back to `QALabSandbox` (it becomes run.json `project` and the persistentDataPath folder). This is a PC checklist step.

## D-024 · 2026-10-04 · Polish session: M7 code before M4–M6, scanner report format, versions stay 0.1.0 (M7)
- **Built early:**
  - ProjectScanner (spec 01, M7).
  - The bot contracts from spec 01's M4 section (`IBotAdapter`, `BotStepResult`, `BotContext`, `BotAdapterRegistry`, `SeededRandom`), so the M7 game-adapter template compiles against the real API.
  - The bot runner, built-in adapters, detectors and screenshots stay in M4.
- **Bot contract details the spec leaves open:**
  - `BotStepResult` is `Continue | Done`.
  - Adapter names follow one rule, snake_case, matched exactly. `CommandLine.IsAdapterName` is shared by `-qalabAdapter` and the registry, so every registered adapter can be selected. A later registration replaces an earlier one, so a game can override a built-in.
  - `BotContext.LogAction` also takes a plain dictionary, so adapters don't build JSON. Strings, numbers and bools are written as they are, a `Vector3` as `[x, y, z]`, anything else as text. Calling it still needs a Newtonsoft reference. Game code without an asmdef has one (the Newtonsoft package's DLL is auto-referenced); an asmdef with Override References must list `Newtonsoft.Json.dll`.
  - Action data goes through the engine-free `BotActionData`, which is checked against the schema example.
- **`scan.json` is not a contract.** It's a Unity-side report that Python never reads, so it has no schema in `schemas/`. It carries `tool` and `format_version: 1`, and its format is documented in `docs/USER_GUIDE.md`. If Python ever reads it, it gets a schema first (contracts first).
- **Scanner rules:**
  - unassigned references are reported for scripts outside Unity's own packages only (`MonoScript` path not under `Packages/com.unity.*`). Built-in components, uGUI and TextMeshPro have many optional slots;
  - a material slot is reported once, as `null_material`, not also as a broken reference;
  - for a `ParticleSystemRenderer` only slot 0 is checked (slot 1 is the trail material, empty unless trails are used);
  - a Build Settings scene whose file is gone is an error (`missing_scene`);
  - loaded scenes are scanned in place, and others are opened additively, then removed again; a scene that was in the Hierarchy unloaded is unloaded again. The scanner never saves anything, and it refuses to run in Play Mode;
  - scene existence is checked through the AssetDatabase (`SceneAsset`), which also finds scenes in registry or git packages;
  - the default output is `Logs/qalab/scan.json` (outside Assets, gitignored by Unity's template);
  - exit codes are 0 clean, 1 errors, 2 the scan failed. `scan_project.ps1` also exits 2 when it couldn't run (no Unity, no scan.json).
- **Packaging:**
  - The editor asmdef references `Newtonsoft.Json.dll` explicitly, like the runtime.
  - The adapter template is a UPM sample (`Samples~`, listed in `package.json`) with no asmdef, so after import it compiles into the game's assembly and can call game code.
  - The template's decision rule is the M7 learning task, stubbed. `TurnPolicy.Decide` is engine-free; its YouWrite tests run in `tools/cs-check` (`SampleTests/TurnPolicyTests.cs`), because the sample is copied into a game where Unity can't run the repo's tests. `MyGameAdapter` only carries decisions out and logs them, and looks the game up on every step, since a destroyed turn manager behind an interface doesn't compare equal to null.
  - `scripts/scan_project.ps1` (not in spec 04's list) runs the scanner on any project.
- **Jenkinsfile:**
  - a Prepare stage deletes `runs\nightly`, `reports\nightly` and `out` (gitignored, so checkout keeps them) so a night never re-triages the previous one;
  - stages for M4/M6 scripts are guarded with `fileExists`, and triage falls back to `samples/sample_run` as a dry run;
  - the provider parameter defaults to `none`;
  - a Gate stage fails the build after publishing when triage exits 3;
  - it's an example: parse-checked with Groovy, never run on Jenkins.
- **Versions stay 0.1.0** in all four places: `python/pyproject.toml`, `qalab.__version__` (triage_meta.json), `package.json` and `QALab.Version` (run.json `qalab_version`). `python/tests/test_versions.py` keeps them equal. No release has been cut. Spec 04 ties version bumps to tags, the first tag is v0.1.0 after M3's PC acceptance, and v1.0.0 needs M4–M7. Everything is listed under `[Unreleased]` in `CHANGELOG.md`. Bumping now would claim releases that don't exist.
- **Hand-off notes:** `docs/progress/` is now one file, `PC_CHECKLIST.md`, with the code state for the next session and every PC step for M0–M8 (`core.md` and `unity.md` are folded in).

## D-025 · 2026-10-04 · M4 runtime: detectors, bot runner, screenshots, results (M4)
- **One frame, fixed order.** The host's Update refreshes the cache, steps the bot, runs the detectors, plans a screenshot, then feeds the metrics. The bot runner is a plain class the host owns, not a MonoBehaviour, so the order never depends on Unity's script execution order.
- **Detector hub:** every report goes through `DetectorHub.Report`: rate limit (same detector and 4 m ground cell, once per 10 s, the same cells triage uses), then a screenshot path, then the event. A report can carry its own position (the tunneling detector reports where the projectile passed through the wall, with the player's position in `details`); the rate-limit cell follows that position. `QALab.ReportDetector` never throws into game code: a bad argument becomes a `[QALab]` warning. A detector that throws is switched off for the rest of the run instead of breaking the game:
  - `NotImplementedException` (a YOU WRITE stub, e.g. `StuckDetector`) is *skipped*: one `[QALab]` warning and `<skipped>` in results.xml, and it doesn't change the exit code. The pipeline works before Sora writes the stuck detector;
  - any other exception is a QA Lab internal error (exit code 2, `<error>` in results.xml).
- **Rules live in engine-free classes** (`RateLimiter`, `ExceptionBurstCounter`, `PerfSpikeDetector`, `FallDetector`, `StuckCalculator`/`StuckDetector`), fed a `DetectorFrame` of plain values, so cs-check tests them. The player position buffer is reused every frame (no per-frame allocation); detectors copy what they keep.
- **Fall:** the kill plane is `QALab.KillPlaneY` if the game sets it, else the lowest renderer in the scene − 5 m, measured once per scene load (finding renderers is too slow per frame). It must sit above the game's own respawn height; the sandbox's floor gives −5.5 m, above its −10 m plane. One report per fall, then `Respawn()`; the bot logs a `respawn` step so repro steps show it.
- **Perf spike:** frames whose time includes QA Lab's own capture are skipped (spec), and so are the 10 frames after each scene load (loading is not a gameplay hitch).
- **Exit code precedence:** 2 (internal error) wins over 1 (blocker/critical): if QA Lab broke, the run is incomplete and CI should look at the tool first.
- **Bot runner:** decisions every 0.25 s; the adapter drives the game's mover through a wrapper that re-issues the last target every frame (the `IBotMover` contract says "every frame while moving") and tells the stuck detector when the bot is trying to move. An adapter that throws stops the bot (internal error) but the run keeps recording. `BotContext` got a second constructor that looks the player up on each access: the game registers its player after the run starts and again after scene reloads.
- **NavMesh explorer, coverage bias (deviation from spec 01's plain random target):** it samples up to 6 reachable random targets and takes the one in the least-visited 4 m cell; choosing a target counts as a visit, so a pocket the player can't really reach doesn't attract it all run. Uniform targets cluster mid-level and rarely reach corners and side rooms, where SB06/SB07 are. It detects respawns (ground distance farther than walking explains) and re-plans, and gives up on a target after 5 s without 0.5 m of progress (or the target's timeout): it logs a `give_up` action (`reason` `no_progress` or `timeout`) and weights that cell as heavily visited, so repro steps show where the bot was blocked and it doesn't keep walking into the same wall.
- **Screenshots:** at most one per frame. Detector events get the path before the file exists (the capture runs at the end of the frame); two detectors in one frame share a file; a due periodic shot folds into it; after a hitch the periodic schedule moves on instead of catching up. A failed capture is an internal error. The PNG encode is synchronous on the main thread (simple and explainable); the next frame is excluded from metrics and perf. Batch mode has no end-of-frame rendering, so it captures at once with `camera_render`; batch mode without a `MainCamera`-tagged camera plans no shot at all (one warning, not an error), so detector events never point to a file that won't exist.
- **Visual labels:** a visual seed counts as triggered only when a screenshot shows it (its trigger time = the shot's). Ground truth for vision should be what a screenshot could show; a black screen between two shots was not observable.
- **results.xml:** one case per built-in detector (always all five, so CI sees a stable list) plus game detectors, `no_exceptions`, and `no_internal_errors` (an `<error>` listing bot/capture/host failures). Not-yet-written detectors are `<skipped>`. Text the XML spec forbids (control characters in an exception message, lone surrogates) is replaced with `?`, so one odd log line can't make the file unreadable.
- **Closing order:** `EndRun` stops the bot, then writes labels.json and results.xml, then the `run_end` marker and run.json. Each step is tried on its own (a failure is an internal error, exit 2, and the other files are still written), and run_end/run.json go last so they carry the exit code the process really returns.
- **Optional modules** are behind version defines: `QALAB_AI` (NavMesh explorer), `QALAB_PHYSICS` (tunneling, line-of-sight), `QALAB_UGUI` (UI crawler), `QALAB_INPUT_SYSTEM` (F12). A game without one of them still compiles the package.
- **Build stamp:** `BuildRunner` writes `StreamingAssets/qalab_build.json` with the commit (`GIT_COMMIT` from Jenkins, else `git rev-parse HEAD`), and run.json `build.git_sha` reads it. It forces Mono for the build and restores the project's backend afterwards.

## D-026 · 2026-10-04 · M4 sandbox: where the seeds are and how they fire (M4)
- **One layout source:** `SandboxLayout` (engine-free) holds every position; the builder, the seed scripts and the catalog's `near` rules use it, and a cs-check test pins SB06 to T_17's centre (35, 0, 1), per D-022. The sample run's SB06 was moved there too (events, labels, two cell tests).
- **SB06 reachable on purpose:** T_17 lies in a 2 m corridor along the south wall that is the only way into the south-east room (x 30–40, z 2–10). Every trip to that room crosses T_17. On an open floor, a back-of-the-envelope estimate (not measured) gave random straight paths only a few percent chance per target of crossing one edge tile. The design doc's corridors (≥ 1.5 m) allow it.
- **SB07 gap width (spec wording):** spec 01 says "narrower than the player's radius but wider than the NavMesh agent radius". Taken literally (0.3–0.4 m) the NavMesh would not connect either. Read as diameters: 0.76 m, between 2 × 0.3 (agent) and 2 × 0.4 (player). The builder sets the Humanoid agent radius to the design doc's 0.3 m (through `ProjectSettings/NavMeshAreas.asset`; there is no public API) and bakes with 5 cm voxels so the 16 cm strip stays connected. With the seed off the post is removed (1.6 m doorway).
- **Seeds stay switchable at runtime** (`-qalabSeeds`): T_17's collider, Crate_07's material, the gap post, the ammo sprite and the projectile collision mode change in Awake/at spawn, not in the scene files.
- **SB08** allocates ~50 MB of small objects on entering GcZone and every 8 s inside, then calls `GC.Collect()` while they are still reachable, so the stall lands in that frame (a real game would pay it at a random later frame). The size is a serialized field: tune it if a fast PC stays under 50 ms.
- **SB10/SB11/SB12:** the camera zone blacks out for 2 s on entry; the score grows 125 points/s and overflows its 136 px box from 10000 (80 s into a run). The score is a legacy uGUI `Text`, and overflow means `preferredWidth` > the box width, instead of TextMeshPro's `isTextOverflowing`: it avoids importing TMP Essentials into the sandbox and stays testable; the ammo panel shows while the ballistics range is active. Each implements `IVisualSeed`.
- **SB16:** the launcher fires only while the player is within 15 m, at 2.5 m height (above the player's 2.1 m), so a bot walking through the range is never in the line of fire and events stay near the range.
- **SB15 and the UI crawler:** the sandbox registers `ui_crawler` with the blocklist Quit, Exit **and Play**: a menu crawl stays in the menus (Play would leave for the level, where there is nothing to click). The level is the explorer's job.

## D-027 · 2026-10-04 · M4 scripts and the one-command pipeline (M4)
- **`run_playtest.ps1` finds its run folder in the player's own log** (`[QALab] recording run <id> to <dir>`), not by listing new folders, so parallel playtests (the Jenkinsfile runs three) can't pick each other's folder. The log is moved into the run folder as `player.log` (spec 00). Exit codes: the player's 0/1/2, plus 3 (no run folder) and 4 (timeout, player stopped). The last output line is the run folder.
- **`run_pipeline.ps1`** builds if the player is missing, runs the explorer in Sandbox_Level01 and then a 30 s UI-crawler run in Sandbox_Menu (`-MenuCrawl 0` skips it) so SB15 can be found in the same command, triages both runs into `reports\<run_id>\`, and exits 3 on a P1. Player exit 1 (a critical detector) is a finding and 2 still leaves data, so only 3+ stops it. The vision step runs only if this `qalab` has `vision analyze` (M6). The provider comes from `qalab.toml` unless `-Provider` is given.
- **Checked here:** all three scripts parse in PowerShell 7.4, and `run_pipeline.ps1` ran end to end under pwsh on Linux with a fake player (a shell script that writes a copy of the sample run and the log line) and the real `qalab` (temporary implementations of the two open Python YOU WRITE functions, restored afterwards): two run folders with `player.log`, a report, exit 3. Not checked: Unity, the real player, Windows paths.

## D-028 · 2026-10-05 · M5 triage evaluation: ground truth and metric definitions (M5)
- **Ground truth per event:**
  - an event is seeded bug X's when X's catalog rule matches it and X is listed in *that run's* `labels.json` (a seed that never fired in a run is not ground truth for it);
  - a rule matches when every key it sets matches (spec 00): `stack_contains`, `message_regex`, `detector`, and `near` + `radius`;
  - `near` is measured on the ground plane (x, z): a fall is reported below the floor (y −5.6), while the catalog's `near` is at floor height;
  - an event that matches two seeds is *ambiguous*: it is counted and left out (the catalog tests keep the rules disjoint, so this should be 0);
  - visual rules (`visual_label`) never match an event: M6 scores them from labelled screenshots;
  - runs without `labels.json` are listed and add no ground truth. Only `qalab.eval` opens `labels.json`.
- **E1, clustering:**
  - pairwise precision, recall and F1 over the clustered events that have ground truth;
  - events matching no seed are counted (`unlabelled_events`) but not scored: they may be real bugs the sandbox didn't seed;
  - pairs are counted (`n(n−1)/2` per group), never enumerated, so 20 runs stay fast;
  - with no predicted pairs, precision is 1.0 (nothing merged wrongly); with no true pairs, recall is 1.0;
  - also reported: cluster-count error (clusters holding labelled events − true bugs: > 0 split, < 0 merged), and, for error analysis, which bugs were split and which clusters mix bugs.
- **E2/E3, reports.** Two subsets:
  - content metrics use the *labelled* reports, whose cluster is mostly (> 50 %) one seeded bug;
  - cost metrics use every report the model was asked for (*attempted*: LLM drafts plus template fallbacks after failed drafts), so a model whose drafts fail isn't flattered by leaving its failures out. With `--provider none` nothing is attempted, so they're `n/a`.
  - *fallback rate* (attempted): the share that fell back to the template;
  - *field completeness* (labelled): the share of the 6 narrative fields with real content, plus at least one step (7 items). Blank, "unknown", "n/a", "none" and "tbd" count as empty; the template writes "unknown" when it can't know a field;
  - *grounding rate* (attempted): the share that are LLM drafts with no grounding failure (unknown evidence, action or doc ids). A fallback counts as not grounded. A test ties the matched wording to `report_llm`'s, so a reworded reason can't silently make this 100%;
  - *repro-step match*: among clusters with bot actions before the first occurrence, the share of reports with at least one `bot_log` step whose action refs all exist;
  - *severity agreement*: exact match with the catalog's `expected_severity`, and within one level;
  - *component correct*: the report's component names one of the backticked components on the feature's `Component:` line in the design doc. Case-insensitive and one way ("door" alone doesn't name `SeededDoor`). `--design-doc` gives that doc without sending it to the model, so RAG-off runs are scored too;
  - *retrieval hit@k* (labelled, spec 02 §10): the share of seeded bugs whose feature heading (the catalog's `feature` is an exact H2 heading of the design doc) is among the top-k chunks retrieved for one of their clusters. A bug split into two clusters counts once. The chunks are recorded as the reports get them, so retrieval doesn't run a second time;
  - *latency p50/p95* (attempted): uncached calls only. *Tokens* (attempted): in + out per report, fallbacks included.
- **Not automated:** whether "expected" matches the design doc needs a human judgment, so EVAL_RESULTS keeps it as a manual column (10 reports, read by Sora).
- **Outputs:**
  - `eval/triage_<label>.json` holds everything;
  - `.md` holds the EVAL_RESULTS tables, to paste as they are;
  - two charts: `_e1_prf.png` and `_reports.png`;
  - `eval/reports_<label>/` holds the reports themselves, for the error analysis.
  - `frame_embed` is skipped with `--provider none`, which has no embeddings. A variant whose provider fails (unreachable, quota) is listed as `failed: <reason>`, and the other variants are still written;
  - the header lists the seeds `labels.json` says fired (visual seeds included) and, separately, the seeds at least one event matched: only the second list is what triage could find.
- **`benchmark.ps1`:**
  - writes `manifest.json` (seeds, durations, git sha, machine, every run with its exit code), UTF-8 without a BOM;
  - CPU/RAM lookups are optional, so a CIM failure can't lose the manifest;
  - each playtest runs in its own try/catch: a failed one makes the script exit 1 but doesn't stop the other seeds;
  - a timed-out playtest's partial folder (no labels.json) moves to `_failed\`, which eval doesn't scan;
  - re-running some seeds into the same folder replaces those seeds' manifest entries and keeps the rest;
  - **scope:** each seed records the 120 s explorer run *and* a 30 s menu crawl (40 runs for 20 seeds, not spec 04's 20), so SB15, which lives in the menu, can be found. `-MenuCrawl 0` gives the spec's 20.
- **Checked here, not measured:**
  - `benchmark.ps1` ran under pwsh on Linux with the fake player from D-027 (copies of the sample run), with temporary implementations of the open YOU WRITE functions, restored afterwards;
  - `qalab eval triage` on that output reproduced EXPECTED.md: `exact` splits SB14, `frame_tfidf` gives 8 clusters for 8 bugs, `tfidf_only` merges SB01 + SB04;
  - that proves the plumbing, not the tool: there are no benchmark numbers until Sora records `seeded_v1` on the PC.

## D-029 · 2026-10-05 · M6 vision: methods, dataset and evaluation discipline (M6)
- **Inference and ground truth stay apart:**
  - `qalab.vision` (analyze, heuristics, VLM, ML, hybrid, findings) never opens `labels.json`, and the leakage test covers `qalab vision analyze`;
  - dataset building and scoring read labels, so they live in `qalab.eval` (`vision_dataset`, `vision_eval`); `qalab vision dataset` is a thin CLI wrapper around them.
- **Heuristics** run on a 640 px copy:
  - `black_ratio` is the share of pixels with luma < 16;
  - `magenta_ratio` is the YOU WRITE task. Its docstring warns about the uint8 trap: `r - b` wraps around in uint8, so cast to int16 first;
  - white boxes: near-white connected components with area, fill and aspect rules;
  - score: 1.0 at or above the threshold, else under 0.5 in proportion to the statistic. Triage keeps labels with score ≥ 0.5, so the threshold decides;
  - `ui_overflow` is not attempted (a known gap, tested by H2).
- **No OpenCV (deviation from spec 03):**
  - connected components come from `scipy.ndimage.label`, already installed with scikit-learn, instead of a 50+ MB package for one function. Its default is 4-connectivity (OpenCV's is 8); a solid box is one component either way;
  - the ML features use Sobel edge density instead of Canny, for the same reason.
- **VLM:**
  - a 768 px PNG, prompt `vision_v1`, the `llm_vision` JSON schema, temperature 0, ≤ 2 retries;
  - a frame whose answers stay unusable gets no labels, and the error is counted, not raised;
  - the cache key covers the prompt text, the image hash, the model and the prompt version. The prompt names the screenshot path, so the same pixels under another path (a dataset copy vs the run folder) are a new call. Spec 03's key leaves the text out; naming the path is harmless for a real model and lets `FakeProvider` answer from a lookup table in offline tests;
- **ML baseline (optional, H4):**
  - 53 features: 3 × 16-bin HSV histograms, edge density, mean and std luma, and the shares of near-white and near-black pixels;
  - one balanced logistic regression per label, its threshold picked on val; a label with no train positives gets no model;
  - saved with joblib. That is a pickle, so only load model files you made yourself;
  - `predict` rescales each probability so the label's val-tuned threshold maps to 0.5 (`calibrated`). Triage keeps scores ≥ 0.5, so `--method ml` keeps exactly what eval measured.
- **Hybrid** (spec 03):
  - heuristics first. The VLM runs only when nothing fired AND the frame is within the window of a UI action (`ui_path`) or a non-visual detector event, or it is the 1st of every N remaining frames;
  - the policy restarts for each run.
- **Dataset:**
  - `index.csv` adds `event_gap_s` (so the hybrid can be simulated offline) and `method` (screen capture vs camera render) to spec 03's columns;
  - seeded 70/15/15 split by run, with at least one val run and one test run once there are 3 runs;
  - `stats.json` warns when a label has fewer than 10 test frames.
- **Evaluation discipline:**
  - the black and magenta thresholds come from fixed grids, picked on val; ties go to the value closest to the spec's starting threshold;
  - the hybrid's N and window are also picked on val: the cheapest setting that keeps ≥ 90% of the VLM-only macro recall (H3), else the best recall;
  - every reported number is on test, plus a `real` split when the dataset has one (M7);
  - the VLM is asked once per frame, and the hybrid is simulated from those answers instead of asking again (temperature 0). Val frames go to the VLM only when the hybrid needs tuning;
  - the ML model trains on train, picks thresholds on val, and reports train vs test macro F1 as an overfitting check.
- **Metrics:**
  - multi-label precision, recall and F1 per label, `n/a` when undefined;
  - macro F1 and macro recall over the labels that have positives in the split;
  - FP per 100 frames, summed over all labels, plus a per-label table (H1 is about two labels);
  - latency p50/p95 per frame and per method, on that split: the heuristics' own time, the ML model's features plus prediction, uncached VLM calls, and for the hybrid the heuristics plus the VLM where it called it;
  - VLM frames with no usable answer (rate limits, bad JSON) are counted per split and flagged in the Markdown: they score as "nothing found";
  - estimated cost only when `[vision] cost_per_1k_images` is set by hand (never invented).
- **Outputs:**
  - `eval/vision_<label>.json` holds everything. Spec 03 says `vision_<date>.json`; the JSON carries `created_at`, and the label names the experiment instead;
  - `.md` holds the EVAL_RESULTS table;
  - two charts: `_prf.png` and `_hybrid_tradeoff.png`;
  - `_errors/` holds thumbnails of 3 false positives and 3 misses of the lead method: the hybrid when it ran, else the VLM, the heuristics, then the ML baseline.
- **Thresholds the tool really uses:** `qalab vision analyze` reads `[vision] black_ratio` and `magenta_ratio` from `qalab.toml` (spec 03's starting values until changed). Eval prints the val-tuned values and, when the configured ones differ, adds a `heuristic (qalab.toml)` row, so EVAL_RESULTS also shows what the shipped settings do.
- **Triage integration:**
  - findings with score ≥ 0.5 become in-memory `visual:<label>` detector events that share their screenshot's seq. `events.jsonl` is never edited;
  - severities: `black_screen` major, the others minor;
  - they cluster like detector events;
  - the best-scoring screenshot is attached first.
  - In eval, a visual event counts as seed X only when `labels.json` says its screenshot shows X with that label. A false positive on a clean frame never counts as finding the bug. Ground truth is keyed by (run, seq, visual label), because two labels on one frame share the screenshot's seq;
  - `qalab vision analyze` counts (and prints) only labels with score ≥ 0.5, the ones triage keeps; the findings file still holds the low scores.
- **Pipeline:**
  - `run_pipeline.ps1` and the Jenkinsfile run `qalab vision analyze` with `qalab.toml`'s method. The default, `heuristic`, is offline and free;
  - a vision failure is a warning (Jenkins: UNSTABLE), and triage still runs.
- **Dependencies:**
  - `pillow`, `scipy` and `joblib` are listed in the core dependencies because qalab imports them. All three were already installed through matplotlib and scikit-learn;
  - the unused `vision` extra (OpenCV) was removed.
- **Checked here, not measured:**
  - with a temporary `magenta_ratio` (restored afterwards), the sample shots match EXPECTED.md: 000004 is `missing_texture`, 000005 is `black_screen`;
  - `evaluate_vision` ran end to end on a 3-run copy of the sample run with the fake VLM;
  - the fake-player pipeline wrote `visual_findings.jsonl`, and triage made 10 clusters (8 + 2 visual);
  - there are no numbers on real sandbox frames until Sora records the benchmark.
