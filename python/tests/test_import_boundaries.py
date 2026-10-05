"""Inference never depends on ground truth: the code that triages runs must not import qalab.eval.

The labels.json leakage test checks files at runtime; this one checks the imports statically, so a
new import of the eval package (which reads labels.json) fails here first.
"""

import ast
from collections.abc import Iterator
from pathlib import Path

import pytest

SRC = Path(__file__).resolve().parents[1] / "src" / "qalab"
INFERENCE = ("triage", "rag", "vision", "io", "report", "llm")


def imported_modules(path: Path) -> Iterator[str]:
    """Every module a file imports: ``import a.b`` → "a.b", ``from a.b import c`` → "a.b"."""
    for node in ast.walk(ast.parse(path.read_text(encoding="utf-8"))):
        if isinstance(node, ast.Import):
            yield from (alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom) and node.module:
            yield ("." * node.level) + node.module


def is_eval(module: str) -> bool:
    return module.lstrip(".").split(".")[:2] == ["qalab", "eval"] or module.lstrip(".") == "eval"


@pytest.mark.parametrize("package", INFERENCE)
def test_inference_never_imports_eval(package: str) -> None:
    folder = SRC / package
    if not folder.is_dir():
        pytest.skip(f"qalab.{package} doesn't exist in this milestone")
    offenders = [
        f"{path.relative_to(SRC)} imports {module}"
        for path in sorted(folder.rglob("*.py"))
        for module in imported_modules(path)
        if is_eval(module)
    ]
    assert offenders == []


def test_the_check_catches_both_import_forms() -> None:
    assert is_eval("qalab.eval.ground_truth") and is_eval("..eval") and is_eval("qalab.eval")
    assert not is_eval("qalab.evaluation") and not is_eval("qalab.triage.eval_helpers")
