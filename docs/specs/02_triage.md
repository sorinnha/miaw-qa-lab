# Spec 02: `qalab triage`, from run folders to ranked, evidence-backed bug reports

**Milestones:** M0 (package skeleton, validate), M2 (core without AI), M3 (LLM + RAG + HTML), M5 (evaluation).

## Goal

Given one or more run folders, produce a short, ranked list of unique bugs a QC lead can trust. Every claim must trace back to events. It should work with no LLM at all (template reports); the LLM makes reports better but is never required.

## CLI (typer)

```
qalab validate <run_dir>...                       schema-check run.json, events.jsonl (+ labels.json if present)
qalab triage run <run_dir|glob>... --out <dir>   full pipeline
      [--docs PATH...] [--repo PATH] [--provider ollama|gemini|openai|anthropic|fake|none]
      [--model NAME] [--cluster exact|frame_tfidf|frame_embed|tfidf_only] [--max-reports 25]
      [--no-cache] [--config qalab.toml]
qalab triage clusters <run_dir>...               debug: print clusters and scores
qalab report html <reports_dir>                  re-render report.html from bugs.json
qalab eval triage <benchmark_dir> [--variants ...] [--provider ...] [--out eval/]
```

Exit codes: 0 = done, no P1 bugs; 3 = done, at least one P1 (CI can fail on this); 2 = error.

## Package layout (`python/src/qalab/`)

```
cli.py  config.py  __main__.py
models/    run.py  event.py  labels.py  visual.py  bug.py       (pydantic v2, mirror schemas/)
io/        runs.py (discover, stream events, validate)  writers.py
triage/    normalize.py (YOU WRITE)  stack.py  signature.py  cluster.py  rank.py
           context.py  report_llm.py  report_template.py  pipeline.py
rag/       chunk.py  index.py  retrieve.py (YOU WRITE: cosine_top_k)  code_context.py
llm/       base.py  factory.py  cache.py  fake.py  ollama.py  gemini.py  openai_.py  anthropic_.py
report/    markdown.py  jira_csv.py  html.py  templates/report.html.j2
prompts/   triage_v1.md  vision_v1.md        (package data, Jinja2)
eval/      metrics.py (YOU WRITE: pairwise_prf)  triage_eval.py  vision_eval.py
vision/    (spec 03)
```

Dependencies: `typer`, `pydantic>=2`, `jsonschema[format]`, `rich`, `numpy`, `scikit-learn`, `httpx`, `jinja2`, `matplotlib`. Dev: `pytest`, `pytest-cov`, `ruff`. Optional extras: `[gemini]`, `[openai]`, `[anthropic]` (official SDKs). Vision adds `opencv-python-headless` and `pillow` (spec 03).

## 1. Load and validate

- Stream `events.jsonl` line by line, validate with the JSON schema, and sort by `seq`.
- Invalid lines are skipped and recorded (`line`, `error`) in `validation_report.json`. Warn if > 1% are invalid. Never crash on one bad line.
- `run.json` without `ended_at` sets `suspected_crash = true` for that run.
- **Never** open `labels.json` here (see the leakage test).

## 2. Normalize messages (YOU WRITE `normalize_message`)

Apply in this order (Python `re`):

| # | What | Pattern | Replace |
|---|---|---|---|
| 1 | GUID | `\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b` | `<guid>` |
| 2 | Hex address | `\b0x[0-9a-fA-F]+\b` | `<hex>` |
| 3 | Quoted value | `'[^']*'` and `"[^"]*"` | `<str>` |
| 4 | Hex id after `_` (must contain a digit) | `(?<=_)(?=[0-9a-f]*\d)[0-9a-f]{6,}\b` | `<id>` |
| 5 | Number not preceded by a letter | `(?<![A-Za-z])\d+(?:\.\d+)?` | `<n>` |
| 6 | Whitespace | `\s+` → one space, then strip | — |

Test cases (Claude Code writes these tests first):

| Input | Output |
|---|---|
| `Inventory slot 7 out of range (size 5)` | `Inventory slot <n> out of range (size <n>)` |
| `KeyNotFoundException: The given key 'enemy_4f2a9c1e' was not present in the dictionary.` | `KeyNotFoundException: The given key <str> was not present in the dictionary.` |
| `Footstep audio clip missing for surface 'Metal'` | `Footstep audio clip missing for surface <str>` |
| `Failed to load asset Assets/Audio/sfx_17.wav` | `Failed to load asset Assets/Audio/sfx_<n>.wav` |
| `Object 0x7ff6a2c1 destroyed` | `Object <hex> destroyed` |
| `Session 3f2b8c1d-9a4e-4b7f-8c2d-1e5f6a7b8c9d expired` | `Session <guid> expired` |
| `enemy_9b03d27f spawned` | `enemy_<id> spawned` |
| `Door_02 is locked` | `Door_<n> is locked` |
| `Vector3 was NaN` | unchanged |
| `Player at (26.4, -12.0, 14.1)` | `Player at (<n>, -<n>, <n>)` |
| `NullReferenceException: Object reference not set to an instance of an object` | unchanged |
| `Texture "Grass_01" not found` | `Texture <str> not found` |
| `  extra   spaces  ` | `extra spaces` |
| `""` (empty) | `""` |

Discuss in DECISIONS.md: over-normalizing (for example, collapsing different asset paths) can merge different bugs.

## 3. Parse stacks

Frame regex (handles `Class.Method` and `Class:Method`, generics, lambdas, optional `(at file:line)`):

```
^(?P<qual>[\w.`\[\],<>+]+?)(?P<sep>[.:])(?P<method>[\w<>`]+) ?\((?P<args>[^)]*)\)(?: \(at (?P<file>.+?):(?P<line>\d+)\))?$
```

- Strip each line before matching. `(at <hash>:0)` means no source location.
- **App frames** exclude anything starting with `UnityEngine.`, `UnityEditor.`, `System.`, `Mono.` or `MiawWorks.QALab.` (our bot must never shape a game bug's signature).
- Normalize to `Qualified.Class.Method` (`:` → `.`); drop args and line numbers.

## 4. Signatures

- `log`: `sha1("log|{level}|{exception_type}|{normalized_message}|{f1}|{f2}|{f3}")[:12]`. `exception_type` = leading `\w+(Exception|Error)` in the message, else empty. f1–f3 = the first 3 app frames.
- `detector`: `sha1("det|{detector}|{scene}|{cell}")[:12]`, where cell = `(floor(x/4), floor(z/4))`. Then merge neighbouring cells of the same detector and scene with DBSCAN (eps 3 m on x, z).
- `visual:<label>`: same as detector.
- Candidates are levels `warning` and up, plus all detectors. `info` logs are context only.
- Line numbers are excluded on purpose: they change between builds and would split clusters. Write that trade-off down.

## 5. Cluster variants (experiment E1, spec 05 in EVAL_RESULTS)

| Variant | Rule after exact grouping |
|---|---|
| `exact` | none |
| `frame_tfidf` | merge two log clusters if they share the top app frame AND TF-IDF cosine of normalized messages ≥ 0.5 |
| `frame_embed` | same, with embeddings, cosine ≥ 0.80 |
| `tfidf_only` | merge if TF-IDF cosine ≥ 0.8, no frame rule. Expected to wrongly merge SB01 and SB04; this shows why frames matter |

Use union-find and a deterministic order (sorted by signature) so results are reproducible.

## 6. Rank (explainable, no ML)

```
w_log  = {exception: 5, assert: 4, error: 3, warning: 1}
w_det  = {blocker: 6, critical: 5, major: 4, minor: 2, trivial: 1}
score  = w × (1 + log2(count)) × (1 + 0.5 × (runs_affected − 1)) × (1.25 if suspected_crash else 1)
P1: score ≥ 15 or any blocker · P2: ≥ 8 · P3: ≥ 3 · P4: below 3        (tunable in qalab.toml)
```

Show the breakdown in `score_breakdown`. The LLM never sets priority.

## 7. Context per cluster (input to the LLM and the template)

IDs are local to one prompt and are mapped back to `{run_id, seq}` afterwards.

- **C:** facts: signature, kind, level, exception_type, count, runs, scenes, builds, first/last `t`, suspected_crash.
- **E1–E5:** representative events: first, last, plus up to 3 with the most different raw messages. Fields: message (≤ 300 chars) and the first 6 app frames.
- **A1–A10:** the last 10 `action` events before the first occurrence in that run (step, action, target / ui_path / object, t).
- **L1–L5:** the last 5 other log lines before the first occurrence.
- **D1–D3:** retrieved doc chunks (id = `file#heading-n`, ≤ 600 chars each).
- **CODE:** ±8 lines around the top app frame (`--repo`), with line numbers.
- Detector details and screenshot paths.
- Budget: about 10,000 characters total. Trim E, then L, then D, in that order.

## 8. LLM report (M3)

- Prompt: `prompts/triage_v1.md` (Jinja2, versioned `triage-v1`). The system part holds the rules; the user part holds the context.
- Request structured output with `schemas/llm_bug_draft.schema.json`, temperature 0.
- Parse with pydantic. On failure, retry up to 2 times, appending "Your previous output failed validation: <error>. Return only valid JSON." If it still fails, use the template report and set `needs_review`.
- **Grounding checks** (each failure adds to `review_reasons`):
  - `evidence_ids` ⊆ provided E ids; drop unknown ones. If none remain, use E1 and flag it.
  - each step with `source = bot_log` must have `action_ids` ⊆ A ids, otherwise it becomes `inferred`.
  - `doc_ids` ⊆ D ids.
  - LLM `severity` differs from the computed priority by more than one level (S1 ↔ P1 …), so it gets flagged.
  - `confidence < 0.5` gets flagged.
- **Template report** (no LLM, also the fallback):
  - title `"{component}: {exception_type or detector} in {top frame}"`;
  - steps built from the A list;
  - expected = `unknown`;
  - `source = template`.

## 9. LLM provider layer

```python
class LLMProvider(Protocol):
    name: str; model: str
    def complete_json(self, system: str, user: str, schema: dict,
                      images: list[bytes] | None = None, temperature: float = 0.0) -> LLMResult: ...
    def embed(self, texts: list[str]) -> np.ndarray: ...   # rows L2-normalized
@dataclass
class LLMResult: data: dict; raw: str; tokens_in: int; tokens_out: int; latency_ms: float; cached: bool
```

- **Ollama** (default, local, free):
  - `POST {OLLAMA_HOST}/api/chat` with `stream: false`, `format: <JSON schema>`, `options.temperature: 0`;
  - images as base64 in the message's `images` field (vision);
  - embeddings via Ollama's embed endpoint (check the current docs).
- **Hosted** (optional extras): use each SDK's structured-output feature. Keys come from env vars only. Never log keys or full prompts containing them.
- **Fake:** deterministic. It builds a valid draft from the context (first E id, all A ids as bot_log steps) and can be told to return invalid JSON N times (to test retries). Embeddings are hashed bag-of-words. All tests and CI use Fake.
- **Cache:** SQLite at `.cache/llm.sqlite`. Key = sha256 of provider, model, prompt_version, system, user, schema and image hashes. `--no-cache` bypasses it.
- **Model choice:** picked in M0 from Sora's hardware (record in ENVIRONMENT.md + DECISIONS.md):
  - text: an instruct model that fits in RAM/VRAM (7–9B class with ≥ 16 GB RAM or ≥ 8 GB VRAM, else 3–4B);
  - embeddings: a dedicated embedding model;
  - vision: a small VLM Ollama supports.
- **Data handling:** default to local models. Some hosted free tiers may use what you send to improve their products, so read the provider's terms and only send sandbox data unless you know it's allowed. Put this in the README under "Data handling".

## 10. RAG (M3)

- **chunk.py:** split markdown on H1–H3, then split long sections into ~800 chars with 100 overlap at sentence boundaries. Chunk id = `{file}#{slug(heading)}-{n}`. Keep the heading text.
- **index.py:** embed in batches of 32 and cache to `.cache/index/<sha256(file contents + model)>.npz` (float32, normalized) plus a metadata JSON. Re-embed only changed files.
- **retrieve.py:** `cosine_top_k(query: np.ndarray, matrix: np.ndarray, k: int) -> list[tuple[int, float]]` (**YOU WRITE**). Rows are pre-normalized, so this is a dot product + argsort. Keep scores ≥ 0.25. With `--provider none`, use the TF-IDF retriever instead.
- **Query:** `"{exception_type} {normalized_message} {top frame} {scene} {detector}"`.
- **code_context.py:**
  - resolve `Assets/...cs:line` against `--repo` (try the root and any `*/Assets/..` match);
  - if there's no line number, find `class {Class}` and then `{Method}(` in that file;
  - return ±8 lines with numbers, capped at 40 lines.
- **Retrieval metric** (eval): hit@3 = share of seeded bugs whose `feature` heading appears among the top-3 chunk headings.

## 11. Outputs (`<out>/`)

- `bugs.json`: array of `qalab.bug_report/1`, sorted by priority then score. Ids `QAL-0001`…
- `report.md`: summary table, then one section per bug.
- `bugs_jira.csv`: columns `Summary, Description, Issue Type, Priority, Labels, Component`. Issue Type = `Bug`; Priority maps P1→Highest, P2→High, P3→Medium, P4→Low; Labels = `qalab;<kind>`.
- `report.html`: one self-contained file (inline CSS/JS, no CDN). It has:
  - counts by priority, a filter, and expandable bugs;
  - screenshot thumbnails (copied into `<out>/shots/`);
  - the evidence table, score breakdown and generator info.
- `clusters.json` (debug), `validation_report.json`, and `triage_meta.json` (timings, provider, model, token totals, cache hits).

## 12. Config: `qalab.toml` (repo root)

```toml
[triage]
min_level = "warning"
cell_size_m = 4.0
dbscan_eps_m = 3.0
max_reports = 25
context_chars = 10000
[triage.merge]
frame_tfidf_threshold = 0.5
frame_embed_threshold = 0.80
tfidf_only_threshold = 0.8
[rank]
p1 = 15.0
p2 = 8.0
p3 = 3.0
crash_multiplier = 1.25
[llm]
provider = "ollama"          # overridden by QALAB_PROVIDER / --provider
temperature = 0.0
max_retries = 2
[rag]
chunk_chars = 800
overlap_chars = 100
top_k = 3
min_score = 0.25
```

## Tests (pytest, offline)

- **normalize:** every row of the table above, plus property tests: idempotent, and never raises.
- **stack:** both formats, generics, lambdas, IL frames, frames without `(at …)`, filtering out QALab/Unity/System frames.
- **signature:** stable across runs; SB01 ≠ SB04; the two SB02 events share one signature.
- **clusters on `samples/sample_run`** (see EXPECTED.md): `exact` = 9, `frame_tfidf` = 8; `tfidf_only` merges SB01 + SB04.
- **rank:** SB13 is last; the crash multiplier applies when `ended_at` is missing (use a copied fixture).
- **context:** A ids are exactly the actions before the first occurrence; the budget trims in order E, L, D.
- **LLM:** Fake returns invalid JSON twice and then valid → attempts = 3. Invalid 3 times → template plus `needs_review`. Unknown evidence ids are dropped and flagged.
- **outputs:** `bugs.json` validates against `bug_report.schema.json`; the CSV header is exact; `report.html` contains every bug id.
- **leakage:** `qalab triage run` and `qalab vision analyze` on the sample run never open `labels.json` (monkeypatch `builtins.open`, `Path.open` and `Path.read_text` to raise on that filename).
- **CLI smoke test:** `qalab triage run samples/sample_run --provider fake --out <tmp>` exits 3 (P1 present) or 0, and produces every output file.

## Performance

- 100k events without an LLM: under 10 s on a laptop, memory proportional to clusters, not events.
- LLM calls only for the top `max_reports` clusters, and cached.

## Acceptance

- **M2:** on `samples/sample_run` and two sandbox runs, every seeded log bug becomes exactly one cluster (except SB14 under `exact`). Template reports are written. Tests pass, with ≥ 80% coverage for `triage/normalize.py`, `stack.py` and `signature.py`.
- **M3:** `qalab triage run samples/sample_run --docs docs/sandbox_design.md --provider ollama` writes valid reports with resolved evidence. Steps cite bot actions. `report.html` opens offline. No test calls a real model. README Quick start works on a fresh clone.
