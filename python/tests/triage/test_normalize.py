"""Spec 02 §2 table for ``normalize_message`` (an M2 learning task, D-030)."""

import pytest

from qalab.triage.normalize import normalize_message

ROWS = [
    ("Inventory slot 7 out of range (size 5)", "Inventory slot <n> out of range (size <n>)"),
    (
        "KeyNotFoundException: The given key 'enemy_4f2a9c1e' was not present in the dictionary.",
        "KeyNotFoundException: The given key <str> was not present in the dictionary.",
    ),
    (
        "Footstep audio clip missing for surface 'Metal'",
        "Footstep audio clip missing for surface <str>",
    ),
    (
        "Failed to load asset Assets/Audio/sfx_17.wav",
        "Failed to load asset Assets/Audio/sfx_<n>.wav",
    ),
    ("Object 0x7ff6a2c1 destroyed", "Object <hex> destroyed"),
    ("Session 3f2b8c1d-9a4e-4b7f-8c2d-1e5f6a7b8c9d expired", "Session <guid> expired"),
    ("enemy_9b03d27f spawned", "enemy_<id> spawned"),
    ("Door_02 is locked", "Door_<n> is locked"),
    ("Vector3 was NaN", "Vector3 was NaN"),
    ("Player at (26.4, -12.0, 14.1)", "Player at (<n>, -<n>, <n>)"),
    (
        "NullReferenceException: Object reference not set to an instance of an object",
        "NullReferenceException: Object reference not set to an instance of an object",
    ),
    ('Texture "Grass_01" not found', "Texture <str> not found"),
    ("  extra   spaces  ", "extra spaces"),
    ("", ""),
]


@pytest.mark.parametrize(("raw", "expected"), ROWS, ids=[r[0][:30] or "empty" for r in ROWS])
def test_normalize_table(raw: str, expected: str) -> None:
    assert normalize_message(raw) == expected


@pytest.mark.parametrize("raw", [r[0] for r in ROWS])
def test_normalize_is_idempotent(raw: str) -> None:
    once = normalize_message(raw)
    assert normalize_message(once) == once


@pytest.mark.parametrize(
    "raw", ["", " ", "'unterminated", "0x", "_", "123", "\n\t", "é ü 日本語 42", "a" * 5000]
)
def test_normalize_never_raises(raw: str) -> None:
    assert isinstance(normalize_message(raw), str)
