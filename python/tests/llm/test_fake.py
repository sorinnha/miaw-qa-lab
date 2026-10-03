import json

import numpy as np
import pytest

from qalab.llm import LLMOutputError, LLMProvider
from qalab.llm.fake import FakeProvider
from qalab.models import LLMBugDraft
from qalab.models.schemas import first_error, load_schema

PROMPT = """CLUSTER
{}
REPRESENTATIVE EVENTS
[E1] t=8.21s scene=Sandbox_Level01 level=exception
message: NullReferenceException
[E2] t=9.0s scene=Sandbox_Level01 level=exception
message: NullReferenceException
ACTIONS BEFORE FIRST OCCURRENCE (from the bot log)
[A1] step 1 t=2.1s move_to target=(4.0, 0.0, 2.0)
[A2] step 3 t=8.2s interact object=Door_02
DESIGN DOCS (retrieved)
[D1] sandbox_design.md > Doors
Doors open when used.
"""


def test_fake_satisfies_protocol() -> None:
    assert isinstance(FakeProvider(), LLMProvider)


def test_fake_builds_a_valid_grounded_draft() -> None:
    provider = FakeProvider()
    result = provider.complete_json("sys", PROMPT, load_schema("llm_bug_draft"))
    assert first_error("llm_bug_draft", result.data) is None
    draft = LLMBugDraft.model_validate(result.data)
    assert draft.evidence_ids == ["E1"]
    assert [s.action_ids for s in draft.steps_to_reproduce] == [["A1"], ["A2"]]
    assert all(s.source == "bot_log" for s in draft.steps_to_reproduce)
    assert draft.doc_ids == ["D1"]
    assert result.tokens_in > 0 and result.tokens_out > 0 and result.cached is False
    assert json.loads(result.raw) == result.data
    assert provider.calls[0]["user"] == PROMPT


def test_fake_invalid_json_mode_then_valid() -> None:
    provider = FakeProvider(invalid_json_times=2)
    schema = load_schema("llm_bug_draft")
    for _ in range(2):
        with pytest.raises(LLMOutputError) as exc:
            provider.complete_json("sys", PROMPT, schema)
        assert exc.value.raw.startswith("{")
    assert provider.complete_json("sys", PROMPT, schema).data["evidence_ids"] == ["E1"]
    assert len(provider.calls) == 3


def test_fake_overrides_and_script() -> None:
    provider = FakeProvider(overrides={"severity": "S4", "confidence": 0.2}, script=['{"a": 1}'])
    schema = load_schema("llm_bug_draft")
    assert provider.complete_json("s", PROMPT, schema).data == {"a": 1}
    data = provider.complete_json("s", PROMPT, schema).data
    assert data["severity"] == "S4" and data["confidence"] == 0.2


def test_fake_embeddings_are_normalized_and_deterministic() -> None:
    provider = FakeProvider()
    matrix = provider.embed(["door opens slowly", "door opens slowly", "inventory slot"])
    assert matrix.dtype == np.float32 and matrix.shape[0] == 3
    np.testing.assert_allclose(np.linalg.norm(matrix, axis=1), 1.0, atol=1e-6)
    np.testing.assert_array_equal(matrix[0], matrix[1])
    assert float(matrix[0] @ matrix[2]) < float(matrix[0] @ matrix[1])
    np.testing.assert_array_equal(matrix[0], FakeProvider().embed(["door opens slowly"])[0])
