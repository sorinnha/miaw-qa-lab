"""Link the QA Lab package into the sandbox project's Packages/manifest.json (M1, spec 01).

Run it once after creating unity/QALabSandbox in Unity Hub:

    python scripts/link_sandbox_package.py            # edit the manifest
    python scripts/link_sandbox_package.py --check    # exit 1 if the manifest is not linked yet

It adds ``"com.miawworks.qalab": "file:../../com.miawworks.qalab"`` to ``dependencies`` and the
package to ``testables`` (so the package's tests show up in the Test Runner). Everything else in the
manifest is kept as Unity wrote it. Running it twice changes nothing.
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[1]
DEFAULT_MANIFEST = REPO / "unity" / "QALabSandbox" / "Packages" / "manifest.json"
PACKAGE = "com.miawworks.qalab"
# Unity resolves it from the Packages folder:
# unity/QALabSandbox/Packages → ../../com.miawworks.qalab = unity/com.miawworks.qalab
PACKAGE_SOURCE = "file:../../com.miawworks.qalab"


def link(manifest: dict) -> list[str]:
    """Add the dependency and the testable in place; return what changed."""
    changes: list[str] = []
    dependencies = manifest.setdefault("dependencies", {})
    if dependencies.get(PACKAGE) != PACKAGE_SOURCE:
        dependencies[PACKAGE] = PACKAGE_SOURCE
        changes.append(f"dependencies.{PACKAGE} = {PACKAGE_SOURCE}")
    testables = manifest.setdefault("testables", [])
    if PACKAGE not in testables:
        testables.append(PACKAGE)
        changes.append(f"testables += {PACKAGE}")
    return changes


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--check", action="store_true", help="only report; exit 1 if not linked")
    args = parser.parse_args(argv)

    if not args.manifest.is_file():
        print(f"no {args.manifest}: create the sandbox project in Unity Hub first (PLAN.md, M1)")
        return 2
    manifest = json.loads(args.manifest.read_text(encoding="utf-8"))
    changes = link(manifest)
    if args.check:
        print("linked" if not changes else "not linked: " + "; ".join(changes))
        return 1 if changes else 0
    if changes:
        # Unity writes the manifest with 2-space indentation and LF line endings.
        text = json.dumps(manifest, indent=2) + "\n"
        args.manifest.write_text(text, encoding="utf-8", newline="\n")
    print("link_sandbox_package: " + ("; ".join(changes) if changes else "already linked"))
    return 0


if __name__ == "__main__":
    sys.exit(main())
