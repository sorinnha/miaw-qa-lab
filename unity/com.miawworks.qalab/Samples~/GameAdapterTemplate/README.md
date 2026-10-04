# Game adapter template

A starting point for a QA Lab bot adapter that plays your game through its own commands. Import it from
**Window → Package Manager → QA Lab → Samples → Game adapter template**. Unity copies it to
`Assets/Samples/QA Lab/<version>/Game adapter template/`.

| File | What to do with it |
|---|---|
| `IGameCommands.cs` | Implement it on your turn manager (or change it to your game's verbs). Set `GameCommandsLocator.Current = this` in `Awake`. |
| `MyGameAdapter.cs` | Rename it, set `AdapterName`, adjust the decision rule. Use only `ctx.Random`. |
| `MyGameQALabBootstrap.cs` | Registers the adapter before the first scene loads. |

There is deliberately no `.asmdef` here: the files compile into your game's main assembly, so they can
call your game code. Then run the game with `-qalab -qalabAdapter my_game`.

The bot runner that calls adapters arrives in QA Lab M4. Until then the adapter is registered and
`run.json` records its name, but nothing calls `Step`.

The full walkthrough is in [docs/GAME_INTEGRATION.md](https://github.com/sorinnha/miaw-qa-lab/blob/main/docs/GAME_INTEGRATION.md).
