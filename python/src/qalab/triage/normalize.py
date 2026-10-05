"""Spec 02 §2: normalize log messages so the same bug with different numbers/ids matches.

Each regex is compiled once here; the comment above it shows a before → after example.
"""

from __future__ import annotations

import re

# "Session 3f2b8c1d-9a4e-4b7f-8c2d-1e5f6a7b8c9d expired" → "Session <guid> expired"
GUID = re.compile(
    r"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b"
)
# "Object 0x7ff6a2c1 destroyed" → "Object <hex> destroyed"
HEX_ADDRESS = re.compile(r"\b0x[0-9a-fA-F]+\b")
# "surface 'Metal'" → "surface <str>"   and   'Texture "Grass_01"' → "Texture <str>"
QUOTED = re.compile(r"'[^']*'|\"[^\"]*\"")
# "enemy_9b03d27f spawned" → "enemy_<id> spawned"   (6+ hex chars after "_" with ≥ 1 digit)
HEX_ID_AFTER_UNDERSCORE = re.compile(r"(?<=_)(?=[0-9a-f]*\d)[0-9a-f]{6,}\b")
# "slot 7 out of range (size 5)" → "slot <n> out of range (size <n>)"   ("Door_02" → "Door_<n>",
# but "Vector3" and "Wave10" stay: a number glued to a letter is part of a name. The lookbehind also
# rejects a digit, or "Wave10" would match from its second digit and become "Wave1<n>".)
NUMBER = re.compile(r"(?<![A-Za-z\d])\d+(?:\.\d+)?")
# "  extra   spaces  " → "extra spaces"
WHITESPACE = re.compile(r"\s+")


def normalize_message(message: str) -> str:
    """Replace volatile parts of a log message with placeholders (spec 02 §2, in table order).

    Learning task, written by Claude at Sora's request (D-030). Applies the module-level patterns in
    this order, each with ``pattern.sub``:
    1. ``GUID`` → ``<guid>``   2. ``HEX_ADDRESS`` → ``<hex>``   3. ``QUOTED`` → ``<str>``
    4. ``HEX_ID_AFTER_UNDERSCORE`` → ``<id>``   5. ``NUMBER`` → ``<n>``
    6. ``WHITESPACE`` → one space, then ``strip()``.
    Must be idempotent (normalizing twice gives the same text) and must never raise: an empty
    string returns an empty string. See ``tests/triage/test_normalize.py`` for every expected row.

    C# comparison: ``Regex.Replace(message, pattern, "<n>")`` chained six times.
    """
    # Order matters: a GUID or 0x address contains digits, so NUMBER must run after them, and a
    # quoted name like 'Grass_01' must become <str> before NUMBER could turn it into 'Grass_<n>'.
    for pattern, placeholder in (
        (GUID, "<guid>"),
        (HEX_ADDRESS, "<hex>"),
        (QUOTED, "<str>"),
        (HEX_ID_AFTER_UNDERSCORE, "<id>"),
        (NUMBER, "<n>"),
    ):
        message = pattern.sub(placeholder, message)
    return WHITESPACE.sub(" ", message).strip()
