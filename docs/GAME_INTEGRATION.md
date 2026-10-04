# Adding QA Lab to a real game

This walks through putting the `com.miawworks.qalab` package into an existing Unity game (M7: Crimson
Tactics, or Dead District), recording runs, writing a game adapter, triaging the runs and scanning the
project. It assumes Unity 2022.3 LTS or newer and Git on your `PATH`.

> **What works today:** recording runs (logs, metrics, markers, `run.json`), the ProjectScanner, triage
> of the runs, and the adapter contracts. **What arrives in M4:** the bot runner that calls adapters,
> detectors and screenshots. Until then an adapter is registered but not driven, so runs are played by hand.

## 0. Decide where the data may go

A real game's logs and stack traces are unreleased work. Triage them with `--provider none` (template
reports, nothing leaves the machine) or `--provider ollama` (local model). Use `--provider gemini` only
for sandbox data, or on a paid tier whose terms allow it. See the README section "Data handling".

## 1. Install the package from Git

**Window → Package Manager → + → Add package from git URL…** and paste:

```
https://github.com/sorinnha/miaw-qa-lab.git?path=/unity/com.miawworks.qalab
```

Pin a version by appending a tag or commit: `...com.miawworks.qalab#v0.1.0` once that tag exists, or
`#<commit sha>` before then. Unity resolves the two dependencies (`com.unity.nuget.newtonsoft-json`,
`com.unity.test-framework`). If the game already pins an older Newtonsoft package, let Unity upgrade it
to 3.2.x.

The same line in `Packages/manifest.json`, if you prefer editing it:

```json
"com.miawworks.qalab": "https://github.com/sorinnha/miaw-qa-lab.git?path=/unity/com.miawworks.qalab"
```

## 2. Record a first run (no bot yet)

1. **Tools → QA Lab → Create or Select Settings.** Tick **Auto-start in Play Mode**. Set **Adapter** to
   `manual` and **Out Dir** to a folder outside `Assets/` (for example `../qalab-runs`).
2. Press Play and play for a minute, then stop. **Tools → QA Lab → Open Last Run Folder** shows the run.
3. Check it from the repo: `qalab validate <run folder>` should print `ok`.

In a built player the same switches are command-line flags; the settings asset is used in the editor
only:

```powershell
CrimsonTactics.exe -qalab -qalabOut D:\qalab-runs -qalabAdapter manual -qalabDuration 300 -logFile D:\qalab-runs\player.log
```

The full flag list is in `docs/USER_GUIDE.md`. Development builds also get script-only stack traces
for warnings and errors, which triage needs.

## 3. Register the player (optional)

If your game has one player avatar, register it once so every event carries a position and, from M4,
so movement-based adapters can steer it:

```csharp
void Start() => MiawWorks.QALab.QALab.RegisterPlayer(transform, this);   // this : IBotMover
```

`IBotMover` has `MoveTowards`, `Stop`, `TryInteract` and `Respawn`. A tactics game with no avatar can
skip this step: events then have `pos: null`, which the tools accept.

## 4. Write the game adapter (YOU WRITE, M7)

An adapter is the bot's strategy. A game adapter plays through the game's **own commands** (select a
unit, read its legal moves, issue one, end the turn) instead of fake clicks. That keeps it robust to UI
changes, and its action log reads like a tester's notes.

1. **Package Manager → QA Lab → Samples → Game adapter template → Import.** The files land in
   `Assets/Samples/QA Lab/<version>/Game adapter template/`. Move them next to your game scripts if you
   like. They have no `.asmdef`, so they compile into your game's assembly and can see its code.
2. Write the decision rule, `TurnPolicy.Decide` (YOU WRITE). It's engine-free, so its tests run
   without Unity: `dotnet test tools\cs-check -c Release --filter "FullyQualifiedName~TurnPolicy"`
   (`tools/cs-check/SampleTests/TurnPolicyTests.cs`). Write it in the repo's copy
   (`unity/com.miawworks.qalab/Samples~/GameAdapterTemplate/TurnPolicy.cs`) until the tests pass, then
   import the sample.
3. Implement `IGameCommands` on the script that owns turns (for example your `TurnManager`):
   - `CanAct`: the player's turn and no animation or dialog in progress;
   - `ActiveUnits()` and `LegalOrders(unit)`: read them from your game rules;
   - `Issue(unit, order)`: the same method a player's click ends up calling;
   - `EndTurn()`.

   In `Awake`, set `GameCommandsLocator.Current = this`; clear it in `OnDestroy`.
4. Rename `MyGameAdapter` and its `AdapterName`, which must be snake_case like `-qalabAdapter` (for
   example `crimson_tactics`). Every random choice comes from `ctx.Random`, so a seed gives the same
   random sequence. The game's state and timing still vary, so the action log is the repro record.
   `MyGameAdapter` carries each decision out and logs it with `ctx.LogAction`; triage turns those action
   events into the report's "steps to reproduce".
5. Run with `-qalabAdapter crimson_tactics` (or set **Adapter** in the settings asset). Until the M4 bot
   runner exists, the name is recorded in `run.json` but the adapter isn't called.

Contracts: `Runtime/Bot/IBotAdapter.cs`, `BotContext.cs`, `BotAdapterRegistry.cs`, `SeededRandom.cs`.

## 5. Triage the runs

From the repo (with the virtual environment active):

```powershell
qalab triage run D:\qalab-runs\* --provider none --repo D:\Games\CrimsonTactics\Assets --docs D:\Games\CrimsonTactics\Docs\design.md --out out\crimson_report
```

- `--repo` adds code context: triage finds the classes named in stack traces in your scripts.
- `--docs` adds design-doc context: Markdown, retrieved by heading.

Open `out\crimson_report\report.html`. Exit code 3 means at least one P1 bug.

## 6. Scan the project

**Tools → QA Lab → Scan Project** writes `Logs/qalab/scan.json` and prints a summary. In batch mode,
from the repo:

```powershell
scripts\scan_project.ps1 -ProjectPath D:\Games\CrimsonTactics -Out out\crimson_scan.json
```

It checks the enabled Build Settings scenes and every prefab for missing scripts, broken references,
empty material slots and error shaders. Unassigned fields of scripts outside Unity's own packages are
listed as info. Exit 1 means it found errors; 2 means it didn't run (see the `.log` next to the output).

## 7. Write it up

Record what you found in `docs/REAL_GAME.md`:
- the runs (count, length, adapter);
- the clusters and which ones are real bugs;
- false positives and why they happened;
- scanner findings;
- what you changed in the game or in QA Lab.

Only numbers you measured go in it.

## Troubleshooting

| Symptom | Fix |
|---|---|
| "Unable to add package … git" | Install Git and restart Unity Hub so it's on `PATH`. |
| CS0012 "Newtonsoft.Json … not referenced" in your adapter | Your adapter sits in an asmdef with **Override References** on: add `Newtonsoft.Json.dll` to its precompiled references (game code without an asmdef already has it). |
| No run folder after Play | Auto-start is off, or Unity runs tests (auto-start is skipped under the Test Runner). |
| `run.json` has no `ended_at` | The game crashed or was killed: triage flags the run as a suspected crash. |
| Adapter not found | The name isn't snake_case or doesn't match `-qalabAdapter`, or the bootstrap file sits in an assembly that isn't loaded. |
| Too many `unassigned_reference` lines in the scan | They're info only: optional fields of your (and third-party) scripts show up there; Unity's own packages are skipped. |
