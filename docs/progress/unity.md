# Unity side: hand-off note

Branch `m1-unity-core` (draft PR "M1: Unity package core + sandbox log bugs"). Covers spec 01's M1 items.
Delete this note when everything below is done.

## Built (M1)

- **Package** `unity/com.miawworks.qalab` (0.1.0): package.json (deps: newtonsoft-json, test-framework),
  README/CHANGELOG/LICENSE, asmdefs for Runtime, Editor, Tests/EditMode, Tests/PlayMode.
  - Engine-free (also compiled in `tools/cs-check`): `CommandLine` + `QALabOptions` + `SeedSelection`,
    `QAEvent`, `StopwatchClock`/`Timestamps`, `IMainThreadState`, `EventWriter`, `RunInfo`/`RunIds`,
    `LogLevels`, `RingBuffer`, `SeedCatalogEntry`/`MatchRule`, `LabelBook`.
  - Unity-only (compiled on the PC): `QALab` facade + bootstrap, `QALabHost`, `MainThreadCache`,
    `RunContext`, `LogCapture`, `MetricsSampler`, `LabelRecorder`, `QALabSettings`, `IBotMover`,
    `Tools > QA Lab` menu, PlayMode `LogCaptureTests`.
- **Sandbox** `unity/QALabSandbox/Assets/Sandbox`: seeds SB01, SB03, SB04, SB05, SB13, SB14 and the
  SB02 YOU WRITE stub, player/interactor/HUD/combat callers, F1 menu, main menu script,
  `SandboxSeedCatalog`, `SandboxSceneBuilder`, asmdefs, EditMode tests.
- **Checks:** `tools/cs-check` (netstandard2.1 core + net8 NUnit), CI job `csharp-check`,
  `scripts/find_unity.ps1`, `scripts/unity_tests.ps1`, `scripts/link_sandbox_package.py`.
- **Decisions:** D-017 … D-020.

## Not verified here (needs Unity on Sora's PC)

Everything Unity-only above compiles only in the editor. The PR's PC checklist lists the exact steps:
create the project, copy, link the package, rebuild scenes, commit generated `.meta`/`.unity` files,
run EditMode/PlayMode tests, play 60 s with F1 → `qalab validate`.

## Open YOU WRITE tasks (Sora)

| File | Function | Test |
|---|---|---|
| `unity/QALabSandbox/Assets/Sandbox/Scripts/SeededBugs/SeededInventory.cs` | `GetSlot` | `SeededInventoryTests` (Unity, category YouWrite) |
| `unity/QALabSandbox/Assets/Sandbox/Scripts/SandboxSeedCatalog.cs` | `SB02()` | `dotnet test tools/cs-check -c Release --filter TestCategory=YouWrite` |

Until `GetSlot` is written, opening the inventory (Tab or F1 → SB02) logs `NotImplementedException: YOU WRITE`.

## Next (M4)

Bot framework (`Bot/`), detectors, screenshots, `JUnitWriter`, exit codes 1/2, seeds SB06–SB12, SB15,
SB16 (+ their catalog entries), `BuildRunner`, QA Lab window, `run_playtest.ps1`, `run_pipeline.ps1`.
The `Cluster`-side Python code already expects `detector` events (spec 02 §4).
