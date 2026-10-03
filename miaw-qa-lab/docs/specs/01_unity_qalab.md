# Spec 01: Unity QA Lab package + sandbox

**Milestones:** M1 (logger core, sandbox log bugs), M4 (bot, detectors, screenshots, runner), M7 (project scanner, game adapter).

## Goal

A Unity package that any project can add. During a playtest it writes a run folder (spec 00), and a seeded bot can drive the game without a human. A sandbox project with known, seeded bugs makes everything measurable.

Non-goals: networking/multiplayer automation, replacing the Unity Profiler, shipping in release builds.

## Versions and dependencies

- Unity: use the version Sora's games use; Unity 6 LTS is preferred for the sandbox. The package must also compile on 2022.3 LTS; guard newer APIs with `#if UNITY_6000_0_OR_NEWER`.
- Package dependencies: `com.unity.nuget.newtonsoft-json` (JSON) and `com.unity.test-framework` (tests).
- Sandbox only: `com.unity.ai.navigation` (NavMeshSurface), uGUI + TextMeshPro, URP (Universal 3D template).

## Package layout

```
unity/com.miawworks.qalab/
  package.json            name com.miawworks.qalab, displayName "QA Lab", version 0.1.0
  README.md  CHANGELOG.md  LICENSE.md
  Runtime/  MiawWorks.QALab.asmdef
    Core/       QALab.cs (static facade), RunContext.cs, CommandLine.cs, QALabSettings.cs,
                QAEvent.cs, EventWriter.cs, Clock.cs, MainThreadCache.cs
    Logging/    LogCapture.cs
    Metrics/    MetricsSampler.cs, RingBuffer.cs
    Capture/    ScreenshotService.cs
    Bot/        IBotAdapter.cs, IBotMover.cs, BotContext.cs, BotRunner.cs, BotAdapterRegistry.cs,
                SeededRandom.cs, NavMeshExplorerAdapter.cs, UICrawlerAdapter.cs
    Detectors/  IDetector.cs, DetectorHub.cs, RateLimiter.cs, StuckDetector.cs (+ StuckCalculator.cs),
                FallDetector.cs, PerfSpikeDetector.cs, ExceptionBurstDetector.cs, TunnelingDetector.cs
    Labels/     LabelRecorder.cs, IVisualSeed.cs, VisualLabelProbe.cs   (benchmark mode only)
    Results/    JUnitWriter.cs
  Editor/   MiawWorks.QALab.Editor.asmdef
    QALabWindow.cs, BuildRunner.cs, ProjectScanner.cs (M7), SandboxSceneBuilder.cs (in the sandbox project, not the package)
  Tests/
    EditMode/  MiawWorks.QALab.Tests.EditMode.asmdef
    PlayMode/  MiawWorks.QALab.Tests.PlayMode.asmdef
```

Install:
- Sandbox: `"com.miawworks.qalab": "file:../../com.miawworks.qalab"` in `unity/QALabSandbox/Packages/manifest.json`, and add `"testables": ["com.miawworks.qalab"]` so the package tests show up.
- Other games: `"com.miawworks.qalab": "https://github.com/sorinnha/miaw-qa-lab.git?path=/unity/com.miawworks.qalab#v0.2.0"`.

## Activation and configuration

The same flags work in a built player and in the editor:

| Flag | Default | Meaning |
|---|---|---|
| `-qalab` | off | Enable QA Lab |
| `-qalabOut <dir>` | `<persistentDataPath>/qalab/runs/<run_id>` | Run folder; the run_id subfolder is created inside `<dir>` |
| `-qalabSeed <int>` | 0 | Seed for the bot RNG |
| `-qalabDuration <s>` | 120 | Stop the bot and end the run after this many seconds |
| `-qalabAdapter <name>` | `navmesh_explorer` | Bot adapter, or `manual` (no bot) |
| `-qalabScene <name>` | active scene | Scene to load first |
| `-qalabShotEvery <s>` | 5 | Periodic screenshots (0 = off) |
| `-qalabMinLevel <lvl>` | `warning` | Lowest log level captured (`info` for more context) |
| `-qalabSeeds <list>` | `all` | Sandbox only: seeded bugs to enable, e.g. `SB01,SB06` |
| `-qalabBenchmark` | off | Write `labels.json` |
| `-qalabQuitOnEnd` | off | Quit the app at the end (exit code below) |

- Editor: a `QALabSettings` asset with the same fields plus "Auto-start in Play Mode", editable from the QA Lab window (`Tools > QA Lab`).
- Bootstrap: `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]`. If enabled, create one `DontDestroyOnLoad` "QALab" object holding the services.
- Games register their player once: `QALab.RegisterPlayer(transform, mover)`, where `mover` implements `IBotMover`.

## Event writing (M1)

- `QAEvent` fields map 1:1 to `qalab.event/1`. Serialize with Newtonsoft using snake_case names, ignore nulls only where the schema allows the field to be missing, and write one compact line per event with `\n`.
- **Thread safety:** `Application.logMessageReceivedThreaded` can fire on any thread, and Unity APIs aren't thread-safe. So:
  - `t` comes from a `System.Diagnostics.Stopwatch` started at run start (thread-safe).
  - `seq` comes from `Interlocked.Increment`.
  - `scene`, `frame` and `pos` are read from `MainThreadCache`: volatile fields updated every frame in `Update` and on `SceneManager.activeSceneChanged`.
  - Events go into a `ConcurrentQueue<QAEvent>`. The main thread drains and appends them every 0.5 s, and on `Application.quitting` / `OnDestroy`.
- **Ordering:** `seq` is assigned when the event is created, so the file can be slightly out of order. Drain into a small buffer sorted by `seq` before writing. Python also sorts by `seq`.
- **Crash tolerance:** at most about 0.5 s of events can be lost. Document this. `run.json` gets `ended_at` only on a clean end.
- **Budget:** under 0.1 ms per frame on average; no per-frame allocations in steady state apart from the events themselves.

## Log capture (M1)

- Map `LogType` to `level` as in spec 00.
- Ignore QA Lab's own logs (prefix `[QALab]`) to avoid feedback loops.
- Development builds set `Application.SetStackTraceLogType`: Log = None, Warning/Error/Exception/Assert = ScriptOnly.
- Info logs are captured only when `-qalabMinLevel info` is set.

## Metrics (M1)

- Every 1 s write a `metric` event: `fps`, `frame_ms` (average), `frame_ms_p95` (ring buffer of frame times), `gc_mb` (`GC.GetTotalMemory(false)`) and `mem_mb` (`Profiler.GetTotalAllocatedMemoryLong`, or omit if it returns 0 in builds).
- Frames where QA Lab captured a screenshot are marked and excluded from frame stats and from the perf detector (the observer effect).

## Screenshots (M4)

- Default method `screen_capture`: in a coroutine, `yield return new WaitForEndOfFrame()`, then `ScreenCapture.CaptureScreenshotAsTexture()` → PNG → `shots/NNNNNN.png`. This includes Screen Space - Overlay UI. Works in a windowed player and in editor Play Mode.
- Fallback `camera_render` (batch mode): render `Camera.main` into a RenderTexture, `ReadPixels`, encode PNG. Overlay UI is missing; record `method` so vision knows.
- **Spike first (M4 step 1):** verify both methods on Sora's machine and record the results in DECISIONS.md. Primary playtests run as a **windowed development player** (that's what QC teams test, and capture is full-fidelity).
- Triggers: periodic (`-qalabShotEvery`), on every detector event (the event's `data.screenshot` points to the file), manual (F12 in development builds). Cap the long side at 1280 px.

## Bot framework (M4)

```csharp
public interface IBotAdapter {
    string Name { get; }
    void Begin(BotContext ctx);
    BotStepResult Step(BotContext ctx);   // called every DecisionInterval (default 0.25 s)
    void End(BotContext ctx);
}
public interface IBotMover {          // implemented by the game's player
    void MoveTowards(Vector3 worldTarget);   // called every frame while moving
    void Stop();
    bool TryInteract(out string objectName); // interact with the nearest interactable, if any
    void Respawn();
}
```

- `BotContext` gives the adapter `SeededRandom Random`, `Transform Player`, `IBotMover Mover`, `float TimeLeft`, and `LogAction(string action, ...)`, which writes an `action` event and increments `step`.
- `BotAdapterRegistry.Register("name", () => new MyAdapter())`. Built-ins register themselves. Games register custom adapters in a bootstrap script.
- **Determinism:** use only `SeededRandom` (wrapping `System.Random(seed)`) in bot code, never `UnityEngine.Random`, which shares state with the game. Physics and frame timing still vary between runs; the action log is the repro record. Replay mode is a stretch goal.
- **`NavMeshExplorerAdapter`**
  - Pick a random point in the NavMesh bounds, then `NavMesh.SamplePosition(..., 5f, NavMesh.AllAreas)`, then `NavMesh.CalculatePath`. Use it only if `PathComplete`.
  - Follow the path corners with `Mover.MoveTowards`, advancing a corner within 0.5 m.
  - Give up on a target after `distance / speed × 2 + 3` seconds.
  - With p = 0.2 per step, call `TryInteract` when something is in range. Log `interact` with the object name.
- **`UICrawlerAdapter`**
  - Each step, collect active, interactable `Selectable`s and pick one with the RNG.
  - Click it with `ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler)`.
  - Log `click` with `ui_path` (hierarchy path).
  - Blocklist names containing `Quit` or `Exit` (configurable).
- **Game adapters (M7):** drive the game through its own commands. For example, a tactics game: pick a unit, get its legal moves, issue a random one, end the turn.

## Detectors (M4)

All detectors go through `DetectorHub.Report(name, severity, details)`. It adds pos and scene, rate-limits (same detector + 4 m cell once per 10 s), requests a screenshot and writes the event.

| Detector | Rule (defaults) | Severity |
|---|---|---|
| `stuck` (**YOU WRITE**) | Bot is moving, but the player moved < 0.5 m in the last 4 s | major |
| `fell_out_of_world` | `pos.y < killPlaneY` (setting, or scene bounds min − 5 m); then `Mover.Respawn()` | critical |
| `perf_spike` | `frame_ms > 50` on a frame without a QA Lab capture | minor |
| `exception_burst` | > 20 exception events within 1 s | major |
| `tunneling` (optional) | Tagged fast projectile: a raycast from the previous to the current position crosses a collider with no collision callback | major |

- Put the math in plain C# classes (for example `StuckCalculator`) so EditMode tests can cover them without a scene.
- Comment why tunneling happens: discrete collision only checks positions per physics step. Fixes: `CollisionDetectionMode.ContinuousDynamic`, thicker colliders, a swept raycast, or a lower speed / smaller fixed timestep.

## Labels: benchmark mode only (M1/M4)

- `LabelRecorder.Trigger("SB06")` is called by seeded scripts and records `t` and scene. At the end it writes `labels.json` using the static catalog (a ScriptableObject with title, type, feature, expected severity and match rules).
- Visual labels: before each screenshot, `VisualLabelProbe` asks every enabled `IVisualSeed` whether it's visible right now:
  - renderer visible and projected bounds ≥ 1% of the screen (missing texture);
  - the black-screen flag is on;
  - TMP `isTextOverflowing` while active (ui_overflow);
  - the placeholder image is active and on screen.
  The probe stores the shot's labels in memory, and they're written to `labels.json` at the end.
- Nothing about labels ever goes into `events.jsonl`.

## Results and exit codes (M4)

- At the end of a run, `JUnitWriter` writes `results.xml`: one test case per detector type and one for "no exceptions". Failures list the counts.
- With `-qalabQuitOnEnd`, `Application.Quit(code)`:
  - `0`: no blocker or critical detector fired.
  - `1`: a blocker or critical detector fired.
  - `2`: QA Lab internal error.

## Sandbox project: `unity/QALabSandbox`

- **Sora creates the project** in Unity Hub (Universal 3D template, same Unity version, location `unity/QALabSandbox`). Claude Code then adds code.
- **Scenes are generated by code:** `Assets/Sandbox/Editor/SandboxSceneBuilder.cs` adds the menu item `Tools > QA Lab > Rebuild Sandbox Scenes`. It builds both scenes procedurally, bakes the NavMesh with `NavMeshSurface.BuildNavMesh()`, and saves them. Commit the generated `.unity` files too. It's reproducible and reviewable, with no hand-merging of scene YAML.
- **`Sandbox_Level01`:** about 40×40 m of floor tiles, walls, 3 doors, crates, a spawner, a GC zone, a camera trigger zone, a ballistics range (thin wall + launcher), an inventory HUD, a score label (TMP) and an ammo icon. NavMeshSurface uses **Render Meshes**, so a tile with a renderer but no collider is still walkable on the NavMesh (SB06).
- **`Sandbox_Menu`:** main menu (Play, Settings, Credits, Quit), a Settings panel (Apply, Back) for the UI crawler.
- **Player:** capsule + CharacterController + `SandboxPlayer : IBotMover`. F1 opens a debug menu to trigger any seed by hand.
- Namespaces: `QALab.Sandbox` (scripts), and seeded bugs in `Assets/Sandbox/Scripts/SeededBugs/`. Each seed lives in its own script so its stack frames are distinctive.

### Seeded bug catalog

| ID | Type | Feature (H2 in sandbox_design.md) | How it's built | Expected detection | Sev |
|---|---|---|---|---|---|
| SB01 | log | Doors | `SeededDoor.Open()` uses an unassigned `hinge` on Door_02 → NullReferenceException | 1 cluster | S2 |
| SB02 | log | Inventory | `SeededInventory.GetSlot(i)` logs `Inventory slot {i} out of range (size 5)` when the HUD uses a stale index | numbers normalized → 1 cluster | S3 |
| SB03 | log | Enemy registry | `SeededEnemyRegistry.Get(id)` throws KeyNotFoundException for random hex ids | ids normalized → 1 cluster | S2 |
| SB04 | log | Spawner | `SeededSpawner.SpawnWave()` NullReferenceException, the same message as SB01 | separate from SB01 (stack) | S2 |
| SB05 | log | Asset loading | `Debug.LogError("Failed to load asset Assets/Audio/sfx_{n}.wav")`, random n | 1 cluster; discuss path normalization | S3 |
| SB06 | detector | Level geometry | Tile T_17 has a renderer but no collider | `fell_out_of_world` near T_17 | S2 |
| SB07 | detector | Level geometry | A gap narrower than the player's radius but wider than the NavMesh agent radius | `stuck` at the gap | S3 |
| SB08 | detector | Performance | GcZone allocates about 50 MB of garbage every 8 s | `perf_spike` | S3 |
| SB09 | visual | Art assets | Crate_07 `sharedMaterial = null` → magenta | `missing_texture` | S3 |
| SB10 | visual | Camera | A trigger zone sets cullingMask = 0 and clears to black for 2 s (HUD still draws) | `black_screen` | S2 |
| SB11 | visual | HUD | Score label overflows its fixed-width box once score ≥ 10000 | `ui_overflow` | S3 |
| SB12 | visual | HUD | Ammo icon Image has no sprite → white box | `placeholder_ui` | S3 |
| SB13 | log | Audio | Warning spam `Footstep audio clip missing for surface '{s}'` | ranked lowest | S4 |
| SB14 | log | Combat math | `MathUtil.SafeDivide` logs `{context}: division by zero in SafeDivide` from two callers | exact splits; merge variants join | S3 |
| SB15 | log | Settings menu | The Settings "Apply" handler throws InvalidOperationException | found by the UI crawler | S2 |
| SB16 | detector | Ballistics range | A fast projectile with Discrete collision tunnels through a 5 cm wall | `tunneling` | S3 |

M1 builds the log seeds (SB01–SB05, SB13, SB14) with manual and scripted triggers. M4 adds SB06–SB12, SB15 and SB16, and bot-driven triggering.

## Build and run (M4)

- `MiawWorks.QALab.Editor.BuildRunner.BuildSandboxPlayer()`, callable with `-executeMethod`. Builds `Builds/Sandbox/QALabSandbox.exe` (Development, Mono, scenes from Build Settings). Exits 0 on success, 1 on failure.
- Player command (wrapped by `scripts/run_playtest.ps1`):
  `QALabSandbox.exe -qalab -qalabOut runs -qalabSeed 42 -qalabDuration 120 -qalabBenchmark -qalabQuitOnEnd -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile runs\player.log`

## Editor tools

- **QA Lab window** (M4): settings, "Play with QA Lab", open the last run folder, open the last report.
- **ProjectScanner** (M7), also runnable as `-executeMethod MiawWorks.QALab.Editor.ProjectScanner.RunFromCommandLine`:
  - scans scenes in Build Settings and all prefabs;
  - **missing scripts** (`GameObjectUtility.GetMonoBehavioursWithMissingScriptCount`);
  - **broken references**: `SerializedProperty` of type ObjectReference with `objectReferenceValue == null && objectReferenceInstanceIDValue != 0`. This is different from **unassigned** (instance id 0), which is reported separately as info;
  - renderers with **null materials** or error shaders.
  - Writes `scan.json` plus console output. Exits 1 if any errors are found.

## Tests

**EditMode:**
- CommandLine parsing (all flags, bad values);
- QAEvent JSON matches `schemas/examples/events_valid.jsonl` (parse and compare);
- SeededRandom is deterministic;
- StuckCalculator math;
- RateLimiter;
- RingBuffer p95;
- JUnitWriter output.

**PlayMode** (load a minimal scene built in code):
- BotRunner with a fake mover for 5 s → ≥ 10 action events, and the player moved;
- FallDetector fires when the player is teleported below the kill plane;
- LogCapture records `Debug.LogError` and an exception thrown in a coroutine;
- every line in `events.jsonl` parses.

Run from the CLI: `scripts/unity_tests.ps1 -Platform EditMode|PlayMode` (wraps `Unity.exe -runTests -batchmode -projectPath unity/QALabSandbox -testPlatform <p> -testResults <file>`; NUnit XML output; don't pass `-quit` with `-runTests`).

**Outside Unity:** EditMode tests of engine-free classes (CommandLine, QAEvent JSON, SeededRandom, StuckCalculator, RateLimiter, RingBuffer, JUnitWriter) also run in `tools/cs-check`, a .NET 8 NUnit 3 project that links the same source and test files (spec 04, D-006). Keep those files free of `UnityEngine`/`UnityEditor`, and find `schemas/` by walking up from the current directory so the tests work in both runners.

## Acceptance

- **M1:** playing `Sandbox_Level01` for 60 s and triggering SB01–SB05, SB13 and SB14 (F1 menu) writes a run folder that passes `qalab validate`. EditMode tests pass.
- **M4:** `scripts/run_pipeline.ps1 -Seed 42 -Duration 120` runs with no manual steps. The run has ≥ 30 actions, at least SB06, SB07 and SB08 detector events, and screenshots with labels. PlayMode tests pass. The screenshot spike is recorded in DECISIONS.md.
- **M7:** the package is installed in Sora's real game by git URL, with a working game adapter, 3 runs triaged, and a ProjectScanner report.
