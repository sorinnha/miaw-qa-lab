"""The VLM path through the fake provider, the cache, and the hybrid policy (no learning task)."""

from pathlib import Path

import numpy as np

from qalab.llm.cache import CachedProvider, LLMCache
from qalab.llm.fake import FakeProvider
from qalab.vision import vlm
from qalab.vision.hybrid import HybridPolicy, event_gaps


def _img(color=(10, 20, 30)) -> np.ndarray:
    img = np.zeros((90, 160, 3), dtype=np.uint8)
    img[:] = color
    return img


def test_vlm_returns_the_labels_the_model_saw() -> None:
    fake = FakeProvider(
        vision_labels={"shots/000004.png": [{"label": "missing_texture", "score": 0.93}]}
    )
    verdict = vlm.analyze(_img(), fake, "shots/000004.png", "Sandbox_Level01", 30.0)
    assert [(f.label, f.score) for f in verdict.labels] == [("missing_texture", 0.93)]
    assert verdict.error is None and verdict.model == "fake-1"
    assert fake.calls[0]["images"] == 1, "the screenshot is sent"
    assert "Screenshot: shots/000004.png." in fake.calls[0]["user"]
    clean = vlm.analyze(_img(), fake, "shots/000001.png", None, None)
    assert clean.labels == []


def test_scores_are_clamped_and_duplicates_keep_the_best() -> None:
    fake = FakeProvider(
        vision_labels={
            "a.png": [
                {"label": "black_screen", "score": 1.7},
                {"label": "black_screen", "score": 0.4},
            ]
        }
    )
    verdict = vlm.analyze(_img(), fake, "a.png", None, None)
    assert [(f.label, f.score) for f in verdict.labels] == [("black_screen", 1.0)]


def test_bad_answers_are_retried_then_reported_not_raised() -> None:
    fake = FakeProvider(script=['{"oops": 1}', "not json", '{"labels": [], "explanation": "fine"}'])
    verdict = vlm.analyze(_img(), fake, "a.png", None, None, max_retries=2)
    assert verdict.error is None and verdict.explanation == "fine"
    broken = FakeProvider(script=["nope"] * 3)
    failed = vlm.analyze(_img(), broken, "a.png", None, None, max_retries=2)
    assert failed.labels == [] and failed.error is not None


def test_second_call_on_the_same_image_is_a_cache_hit(tmp_path: Path) -> None:
    inner = FakeProvider(vision_labels={"a.png": [{"label": "placeholder_ui", "score": 0.8}]})
    cached = CachedProvider(inner, LLMCache(tmp_path / "llm.sqlite"), prompt_version="vision-v1")
    first = vlm.analyze(_img(), cached, "a.png", None, None)
    second = vlm.analyze(_img(), cached, "a.png", None, None)
    assert not first.cached and second.cached
    assert len(inner.calls) == 1
    vlm.analyze(_img((200, 0, 0)), cached, "a.png", None, None)
    assert len(inner.calls) == 2, "different pixels, different key"


def test_hybrid_calls_near_events_and_every_nth_other_frame() -> None:
    policy = HybridPolicy(every_n=3, window_s=2.0)
    decisions = [
        policy.should_call_vlm(True, 0.5),  # a heuristic fired: never
        policy.should_call_vlm(False, 1.0),  # near a UI action: always
        policy.should_call_vlm(False, None),  # remaining #1: call
        policy.should_call_vlm(False, 9.0),  # remaining #2
        policy.should_call_vlm(False, 9.0),  # remaining #3
        policy.should_call_vlm(False, 9.0),  # remaining #4: call
    ]
    assert decisions == [False, True, True, False, False, True]


def test_event_gaps_find_the_nearest_event() -> None:
    assert event_gaps([0.0, 5.0, 12.0], [4.0, 11.0]) == [4.0, 1.0, 1.0]
    assert event_gaps([1.0], []) == [None]
