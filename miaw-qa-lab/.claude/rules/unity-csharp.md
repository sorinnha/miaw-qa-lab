---
paths:
  - "unity/**/*.cs"
  - "unity/**/*.asmdef"
  - "unity/**/package.json"
  - "unity/**/manifest.json"
---

# Unity / C# rules (QA Lab package + sandbox)

- Namespaces: `MiawWorks.QALab.*` for the package, `QALab.Sandbox.*` for the sandbox project.
- One assembly definition per folder (Runtime, Editor, Tests/EditMode, Tests/PlayMode). Editor-only code goes in the Editor asmdef or inside `#if UNITY_EDITOR`.
- **No Unity API calls off the main thread.** Log callbacks can run on any thread: read only from `MainThreadCache`, enqueue, and return.
- Hot paths (Update, log callback) don't allocate per frame except for the event objects themselves. Cache component lookups and avoid LINQ in Update.
- Prefer `[SerializeField] private` over public fields. Public API gets XML doc comments.
- Bot code uses `SeededRandom` only, never `UnityEngine.Random`.
- Keep logic testable: put math and rules (stuck window, rate limiter, p95) in plain C# classes covered by EditMode tests. MonoBehaviours just wire them up.
- Keep those classes and their tests **engine-free** (no `UnityEngine`/`UnityEditor`; plain floats instead of `Vector3` where practical), so `tools/cs-check` can compile and test them with .NET 8 outside Unity, in CI and in cloud sessions. C# YOU WRITE tests get `[Category("YouWrite")]` until `/review-mine` passes them.
- Guard version-specific APIs with `#if UNITY_6000_0_OR_NEWER` so the package also compiles on 2022.3 LTS.
- QA Lab's own logs start with `[QALab]` and are ignored by LogCapture.
- Sandbox scenes are built by `SandboxSceneBuilder` (editor menu). Change the builder, not the scene by hand, then rebuild and commit both.
- Seeded bugs live in `Assets/Sandbox/Scripts/SeededBugs/`, one per script, each calling `LabelRecorder.Trigger("SBxx")`. Never hide a seed's id in log text.
- Every behaviour change gets an EditMode or PlayMode test. Run them with `scripts/unity_tests.ps1`.
- Sora is comfortable in C#. In Unity code, explain *why* (threading, lifecycle, physics) more than syntax.
