"""scripts/link_sandbox_package.py edits the sandbox manifest idempotently."""

import importlib.util
import json
from pathlib import Path
from types import ModuleType

REPO = Path(__file__).resolve().parents[2]


def _script() -> ModuleType:
    script = REPO / "scripts" / "link_sandbox_package.py"
    spec = importlib.util.spec_from_file_location("link", script)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def _manifest(tmp_path: Path, data: dict) -> Path:
    path = tmp_path / "manifest.json"
    path.write_text(json.dumps(data, indent=2), encoding="utf-8")
    return path


def test_links_dependency_and_testable_keeping_everything_else(tmp_path: Path) -> None:
    link = _script()
    urp = "com.unity.render-pipelines.universal"
    path = _manifest(tmp_path, {"dependencies": {urp: "17.0.3"}, "scopedRegistries": []})
    assert link.main(["--manifest", str(path)]) == 0
    data = json.loads(path.read_text(encoding="utf-8"))
    assert data["dependencies"]["com.miawworks.qalab"] == "file:../../com.miawworks.qalab"
    assert data["dependencies"][urp] == "17.0.3"
    assert data["testables"] == ["com.miawworks.qalab"]
    assert data["scopedRegistries"] == []
    raw = path.read_bytes()
    assert b"\r" not in raw and raw.endswith(b"\n")


def test_second_run_changes_nothing_and_check_passes(tmp_path: Path) -> None:
    link = _script()
    path = _manifest(tmp_path, {"dependencies": {}, "testables": ["com.unity.inputsystem"]})
    assert link.main(["--manifest", str(path), "--check"]) == 1
    assert link.main(["--manifest", str(path)]) == 0
    first = path.read_text(encoding="utf-8")
    assert link.main(["--manifest", str(path)]) == 0
    assert path.read_text(encoding="utf-8") == first
    assert link.main(["--manifest", str(path), "--check"]) == 0
    assert json.loads(first)["testables"] == ["com.unity.inputsystem", "com.miawworks.qalab"]


def test_missing_manifest_is_reported(tmp_path: Path) -> None:
    assert _script().main(["--manifest", str(tmp_path / "nope.json")]) == 2


def test_package_source_points_at_the_package_folder() -> None:
    link = _script()
    packages_dir = REPO / "unity" / "QALabSandbox" / "Packages"
    resolved = (packages_dir / link.PACKAGE_SOURCE.removeprefix("file:")).resolve()
    assert resolved == (REPO / "unity" / "com.miawworks.qalab").resolve()
    assert (resolved / "package.json").is_file()
    package_json = json.loads((resolved / "package.json").read_text(encoding="utf-8"))
    assert package_json["name"] == link.PACKAGE
