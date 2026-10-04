using MiawWorks.QALab;
using UnityEngine;

namespace MyGame.QALab
{
    /// <summary>
    /// Registers the adapter once, before the first scene loads, so <c>-qalabAdapter my_game</c> can find it.
    /// Keep this file in your game's main assembly (no .asmdef in this folder), so it can see your game code.
    /// </summary>
    public static class MyGameQALabBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Register()
        {
            BotAdapterRegistry.Register(MyGameAdapter.AdapterName, () => new MyGameAdapter(FindGame));
        }

        /// <summary>The current turn manager, or null if there is none or it was destroyed without clearing the locator.</summary>
        private static IGameCommands FindGame()
        {
            var game = GameCommandsLocator.Current;
            // Unity's "destroyed equals null" only works through a UnityEngine.Object reference.
            return game is Object unityObject && unityObject == null ? null : game;
        }
    }
}
