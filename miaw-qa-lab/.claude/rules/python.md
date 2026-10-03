---
paths:
  - "python/**/*.py"
  - "python/pyproject.toml"
---

# Python rules (qalab)

- Python 3.12. Type hints on every function. Run `ruff check` and `ruff format` clean.
- Data models: pydantic v2 in `qalab/models/` mirror `schemas/*.json`. Validate files with `jsonschema` against the schema files themselves; a test checks that both accept and reject the same fixtures.
- Use `pathlib.Path` everywhere. No string path joins, no hardcoded absolute paths.
- Library code uses `logging`. Only `cli.py` prints, through `rich`.
- Small functions, each doing one thing. Name things for what they mean in QA (`cluster`, `signature`, `evidence`), not generic names (`data2`, `helper`).
- Regexes are compiled once at module level, and each has a comment with a before → after example.
- Stream large files (`for line in f`). Never load all events into a list unless the function's job is sorting a small subset.
- LLM code goes only through `qalab.llm.base.LLMProvider`. Tests use `FakeProvider` and never touch the network.
- Tests live in `python/tests/`, mirroring module paths (`tests/triage/test_normalize.py`). Fixtures come from `samples/sample_run` and `schemas/examples`.
- Tests of a YOU WRITE function, and of code that calls it, get `@pytest.mark.youwrite`. `tests/conftest.py` makes them xfail while the stub raises `NotImplementedError`. `/review-mine` removes the marker once Sora's version passes.
- Inference modules (`qalab.triage`, `qalab.vision`, `qalab.rag`) must never import from `qalab.eval` or open `labels.json`.
- Ask Sora before adding a new dependency. Say why it's worth it.
- When Sora is new to a Python feature (comprehensions, generators, dataclasses, context managers, typing.Protocol), add a short comment comparing it to the C# equivalent the first time it appears.
