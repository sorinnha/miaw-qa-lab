from qalab.triage.stack import Frame, app_frames_of, parse_frame, parse_stack

EXCEPTION_STYLE = (
    "QALab.Sandbox.SeededDoor.Open () (at Assets/Sandbox/Scripts/SeededBugs/SeededDoor.cs:27)\n"
    "QALab.Sandbox.Interactor.TryInteract (UnityEngine.GameObject target) "
    "(at Assets/Sandbox/Scripts/Interactor.cs:41)\n"
    "MiawWorks.QALab.Bot.NavMeshExplorerAdapter.Step (MiawWorks.QALab.Bot.BotContext ctx) "
    "(at Packages/com.miawworks.qalab/Runtime/Bot/NavMeshExplorerAdapter.cs:88)\n"
    "MiawWorks.QALab.Bot.BotRunner.Update () "
    "(at Packages/com.miawworks.qalab/Runtime/Bot/BotRunner.cs:64)"
)
DEBUG_LOG_STYLE = (
    "UnityEngine.Debug:LogError (object)\n"
    "QALab.Sandbox.SeededInventory:GetSlot (int) "
    "(at Assets/Sandbox/Scripts/SeededBugs/SeededInventory.cs:33)\n"
    "QALab.Sandbox.HudInventory:Refresh () (at Assets/Sandbox/Scripts/HudInventory.cs:19)"
)
IL_FIRST = (
    "System.Collections.Generic.Dictionary`2[TKey,TValue].get_Item (TKey key) "
    "(at <b2e5f6a1d3c94e0f9a7b8c6d5e4f3a2b>:0)\n"
    "QALab.Sandbox.SeededEnemyRegistry.Get (System.String id) "
    "(at Assets/Sandbox/Scripts/SeededBugs/SeededEnemyRegistry.cs:18)"
)


def test_exception_style_and_bot_filtering() -> None:
    frames = parse_stack(EXCEPTION_STYLE)
    assert len(frames) == 4
    assert frames[0] == Frame(
        "QALab.Sandbox.SeededDoor.Open", "Assets/Sandbox/Scripts/SeededBugs/SeededDoor.cs", 27
    )
    app = app_frames_of(EXCEPTION_STYLE)
    assert [f.qualified for f in app] == [
        "QALab.Sandbox.SeededDoor.Open",
        "QALab.Sandbox.Interactor.TryInteract",
    ]


def test_debug_log_style_normalizes_colon() -> None:
    app = app_frames_of(DEBUG_LOG_STYLE)
    assert [f.qualified for f in app] == [
        "QALab.Sandbox.SeededInventory.GetSlot",
        "QALab.Sandbox.HudInventory.Refresh",
    ]
    assert app[0].line == 33 and app[0].class_name == "SeededInventory"
    assert app[0].method == "GetSlot"


def test_il_frame_generics_and_system_filter() -> None:
    frames = parse_stack(IL_FIRST)
    assert frames[0].qualified == "System.Collections.Generic.Dictionary`2[TKey,TValue].get_Item"
    assert frames[0].file is None and frames[0].line is None
    assert not frames[0].is_app
    assert app_frames_of(IL_FIRST)[0].qualified == "QALab.Sandbox.SeededEnemyRegistry.Get"


def test_lambda_release_and_garbage_lines() -> None:
    lambda_frame = parse_frame("Game.Combat.Weapon+<>c.<Fire>b__3_0 (Game.Combat.Target t)")
    assert lambda_frame is not None and lambda_frame.is_app
    assert lambda_frame.qualified == "Game.Combat.Weapon+<>c.<Fire>b__3_0"
    release = parse_frame("  QALab.Sandbox.SeededDoor.Open ()  ")
    assert release == Frame("QALab.Sandbox.SeededDoor.Open")
    assert str(release) == "QALab.Sandbox.SeededDoor.Open"
    assert parse_frame("Rethrow as TargetInvocationException: boom") is None
    assert parse_stack(None) == [] and parse_stack("") == []
    assert [f.qualified for f in parse_stack("junk line\n" + DEBUG_LOG_STYLE)][0].startswith(
        "UnityEngine"
    )
