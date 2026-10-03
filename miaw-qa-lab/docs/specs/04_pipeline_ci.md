# Spec 04: Scripts, pipeline, CI and releases

**Milestones:** M0 (Python CI), M1 (Unity test script), M4 (build, playtest, pipeline), M5 (benchmark), M7 (Jenkinsfile, release v1.0.0).

## PowerShell scripts (`scripts/`), Windows-first

All scripts: `param(...)` with defaults, `$ErrorActionPreference = "Stop"`, paths built from `$PSScriptRoot`, a non-zero exit on failure, and a one-line summary at the end.

| Script | Does |
|---|---|
| `find_unity.ps1` | Returns `Unity.exe`: `$env:UNITY_EXE`, else Unity Hub's default editor folder (`C:\Program Files\Unity\Hub\Editor\<ver>\Editor\Unity.exe`) matching `unity/QALabSandbox/ProjectSettings/ProjectVersion.txt` |
| `unity_tests.ps1 -Platform EditMode\|PlayMode` | `Unity.exe -runTests -batchmode -projectPath unity\QALabSandbox -testPlatform <p> -testResults out\<p>.xml -logFile out\<p>.log` (no `-quit` with `-runTests`). Prints pass/fail counts from the NUnit XML |
| `build_sandbox.ps1` | `Unity.exe -batchmode -quit -projectPath unity\QALabSandbox -executeMethod MiawWorks.QALab.Editor.BuildRunner.BuildSandboxPlayer -logFile builds\build.log` |
| `run_playtest.ps1 -Seed 42 -Duration 120 [-Adapter navmesh_explorer] [-Scene Sandbox_Level01] [-Benchmark] [-Out runs]` | Runs the built player with the spec 01 flags, waits, and prints the run folder. Exit code passes through |
| `run_pipeline.ps1 -Seed 42 -Duration 120 [-Provider ollama] [-Open]` | Builds if missing → playtest → `qalab vision analyze` → `qalab triage run` → opens `report.html`. Exit 3 if any P1 |
| `benchmark.ps1 -Seeds 1..20 -Duration 120 [-Out benchmarks\seeded_v1]` | Loops playtests with `-Benchmark`, then writes `benchmarks\seeded_v1\manifest.json` (seeds, durations, git sha, machine) |

A bash equivalent is optional and only needed if Sora wants CI on Linux for the Python side.

## Python environment

```powershell
py -3.12 -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -e ".\python[dev]"          # add ,gemini / ,openai / ,anthropic as needed
```

`pyproject.toml` lives in `python/` and exposes the console script `qalab = qalab.cli:app`. Configure ruff (line length 100; rules E, F, I, B, UP) and pytest (`testpaths = ["tests"]`) there.

## GitHub Actions (`.github/workflows/python-ci.yml`)

- Triggers: push and pull_request.
- Matrix: `ubuntu-latest`, `windows-latest`; Python 3.12.
- Steps: checkout → setup-python (pip cache) → `pip install -e "./python[dev]"` → `ruff check python` → `ruff format --check python` → `pytest python -q --cov=qalab --cov-report=xml` → smoke test:
  - `qalab validate samples/sample_run`
  - `qalab triage run samples/sample_run --provider fake --out out/ci_report` (accept exit 0 or 3)
- Upload `out/ci_report` and `coverage.xml` as artifacts. Add the status badge to the README.
- **`csharp-check` job** (from M1, `ubuntu-latest`): `actions/setup-dotnet` 8.0 → `dotnet test tools/cs-check -c Release --filter "TestCategory!=YouWrite"`. It compiles the engine-free package code (`LangVersion 9`, like Unity) and runs its EditMode tests outside Unity (D-006).
- No secrets are needed: CI never calls a real model.

**Unity tests in CI (stretch):** GameCI's test runner needs Unity license secrets. Document the steps in `docs/CI_UNITY.md` if attempted; otherwise Unity tests run locally via `unity_tests.ps1` and their XML is attached to releases.

## Jenkins example (`ci/Jenkinsfile`, M7)

A declarative pipeline for a Windows agent with Unity installed, mirroring how a studio would run QA Lab nightly:

```
triggers { cron('H 2 * * *') }
stages:
  Checkout
  Python setup         (venv + pip install)
  Unity EditMode tests (unity_tests.ps1 → publish NUnit XML)
  Build sandbox        (build_sandbox.ps1)
  Playtests            (parallel: seeds 1, 2, 3 via run_playtest.ps1 -Benchmark)
  Vision + Triage      (qalab vision analyze; qalab triage run runs\* --out reports\nightly)
  Publish              (archiveArtifacts reports/**, runs/**/results.xml; publish report.html)
post:
  failure if the triage exit code is 3 (a P1 bug)
```

Label it in the README as an **example**, unless Sora actually runs Jenkins (for example in Docker) and can show a screenshot of a green run.

## Releases

| Tag | After | Contents |
|---|---|---|
| v0.1.0 | M3 | Unity logger + triage with AI reports (send to recruiters) |
| v0.2.0 | M4 | Bot + detectors + one-command pipeline |
| v0.3.0 | M5 | Triage evaluation results |
| v0.4.0 | M6 | Vision module + evaluation |
| v1.0.0 | M7 | Real-game integration, scanner, polish, demo video |

For each release: update `CHANGELOG.md` (Keep a Changelog format) and the package version in `package.json`, tag (`git tag -a vX.Y.Z`), and create a GitHub Release with `report.html` from the sample or sandbox attached. Ask Sora before tagging and pushing.

## Acceptance

- **M0:** CI is green on both OSes; the README badge works.
- **M4:** `run_pipeline.ps1` succeeds end to end on Sora's PC from a clean clone, after building the sandbox once.
- **M7:** `ci/Jenkinsfile` is present and documented; v1.0.0 is released.
