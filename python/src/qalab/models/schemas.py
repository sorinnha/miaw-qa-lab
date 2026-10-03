"""Locate and compile the JSON Schemas in ``schemas/``.

The schema files are the source of truth for the Unity ↔ Python contract, so Python validates
raw JSON against them with ``jsonschema`` (Draft 2020-12) before building pydantic models.
"""

from __future__ import annotations

import json
import os
from functools import cache
from pathlib import Path
from typing import Any

from jsonschema import Draft202012Validator, FormatChecker

SCHEMA_NAMES = (
    "event",
    "run",
    "labels",
    "bug_report",
    "llm_bug_draft",
    "llm_vision",
    "visual_finding",
)


def schemas_dir() -> Path:
    """Return the ``schemas/`` folder: ``QALAB_SCHEMAS_DIR`` if set, else the repo's folder."""
    override = os.environ.get("QALAB_SCHEMAS_DIR")
    if override:
        return Path(override)
    # python/src/qalab/models/schemas.py → repo root is four parents up.
    return Path(__file__).resolve().parents[4] / "schemas"


@cache
def load_schema(name: str) -> dict[str, Any]:
    """Load ``schemas/<name>.schema.json`` once and keep it in memory."""
    path = schemas_dir() / f"{name}.schema.json"
    with path.open(encoding="utf-8") as f:
        return json.load(f)


@cache
def validator_for(name: str) -> Draft202012Validator:
    """Compiled validator with format checking, so ``date-time`` is really checked."""
    return Draft202012Validator(load_schema(name), format_checker=FormatChecker())


def first_error(name: str, instance: Any) -> str | None:
    """Return the first schema violation as text, or None when the instance is valid."""
    error = next(iter(validator_for(name).iter_errors(instance)), None)
    if error is None:
        return None
    where = "/".join(str(p) for p in error.absolute_path) or "<root>"
    return f"{where}: {error.message}"
