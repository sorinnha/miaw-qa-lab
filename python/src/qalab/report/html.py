"""``report.html``: one self-contained page (inline CSS/JS, no CDN) rendered with Jinja2."""

from __future__ import annotations

import json
from collections.abc import Iterable, Mapping
from importlib import resources
from pathlib import Path
from typing import Any

from jinja2 import Environment, StrictUndefined, select_autoescape

from qalab.models.bug import BugReport

_env = Environment(undefined=StrictUndefined, autoescape=select_autoescape(["html", "j2"]))


def render_html(reports: Iterable[BugReport], meta: Mapping[str, Any]) -> str:
    reports = list(reports)
    counts = {p: sum(1 for r in reports if r.priority == p) for p in ("P1", "P2", "P3", "P4")}
    template = _env.from_string(
        resources.files("qalab.report.templates").joinpath("report.html.j2").read_text("utf-8")
    )
    return template.render(
        reports=[r.model_dump(by_alias=True, mode="json") for r in reports],
        meta=dict(meta),
        counts=counts,
        meta_json=json.dumps(dict(meta), indent=1, default=str),
    )


def load_reports(bugs_json: Path) -> list[BugReport]:
    data = json.loads(bugs_json.read_text(encoding="utf-8"))
    return [BugReport.model_validate(item) for item in data]


def rerender_html(reports_dir: Path) -> Path:
    """``qalab report html``: rebuild report.html from bugs.json (+ triage_meta.json)."""
    reports = load_reports(reports_dir / "bugs.json")
    meta_path = reports_dir / "triage_meta.json"
    meta = json.loads(meta_path.read_text(encoding="utf-8")) if meta_path.is_file() else {}
    target = reports_dir / "report.html"
    target.write_text(render_html(reports, meta), encoding="utf-8", newline="\n")
    return target
