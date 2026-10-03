"""Load the versioned Jinja2 prompts shipped in ``qalab/prompts/`` (spec 02 §8)."""

from __future__ import annotations

import re
from dataclasses import dataclass
from functools import cache
from importlib import resources
from typing import Any

from jinja2 import Environment, StrictUndefined

# "{# prompt_version: triage-v1 · Jinja2 · ... #}" → "triage-v1"
_VERSION = re.compile(r"prompt_version:\s*(?P<version>[\w.-]+)")
# Splits "## system\n...\n## user\n..." into its two parts.
_SECTION = re.compile(r"^## (?P<name>system|user)\s*$", re.MULTILINE)

_env = Environment(undefined=StrictUndefined, keep_trailing_newline=True, autoescape=False)


@dataclass(frozen=True)
class PromptTemplate:
    name: str
    version: str
    system: str
    user_template: str

    def render_system(self) -> str:
        return _env.from_string(self.system).render().strip()

    def render_user(self, **variables: Any) -> str:
        return _env.from_string(self.user_template).render(**variables).strip()


@cache
def load_prompt(name: str = "triage_v1") -> PromptTemplate:
    text = resources.files("qalab.prompts").joinpath(f"{name}.md").read_text(encoding="utf-8")
    version_match = _VERSION.search(text)
    if not version_match:
        raise ValueError(f"prompt {name} has no prompt_version comment")
    parts = _SECTION.split(text)
    # parts = [preamble, "system", system_text, "user", user_text]
    sections = dict(zip(parts[1::2], parts[2::2], strict=True))
    if set(sections) != {"system", "user"}:
        raise ValueError(f"prompt {name} must have exactly a ## system and a ## user section")
    return PromptTemplate(
        name=name,
        version=version_match.group("version"),
        system=sections["system"].strip(),
        user_template=sections["user"].strip(),
    )
