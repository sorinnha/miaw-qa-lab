"""The version is written in four places; a release must change them together (D-024)."""

import json
import re
import tomllib
from pathlib import Path

import qalab

REPO = Path(__file__).resolve().parents[2]
# 'public const string Version = "0.1.0";' → "0.1.0"
CSHARP_VERSION = re.compile(r'public const string Version = "([^"]+)";')


def test_python_unity_and_package_versions_agree() -> None:
    pyproject = tomllib.loads((REPO / "python" / "pyproject.toml").read_text(encoding="utf-8"))
    package = json.loads(
        (REPO / "unity" / "com.miawworks.qalab" / "package.json").read_text(encoding="utf-8")
    )
    facade = (REPO / "unity" / "com.miawworks.qalab" / "Runtime" / "Core" / "QALab.cs").read_text(
        encoding="utf-8"
    )
    match = CSHARP_VERSION.search(facade)
    assert match, "QALab.Version not found"
    versions = {
        "pyproject.toml": pyproject["project"]["version"],
        "qalab.__version__": qalab.__version__,
        "package.json": package["version"],
        "QALab.Version": match.group(1),
    }
    assert len(set(versions.values())) == 1, versions
