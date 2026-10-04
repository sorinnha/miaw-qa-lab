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
            BotAdapterRegistry.Register(MyGameAdapter.AdapterName,
                () => new MyGameAdapter(() => GameCommandsLocator.Current));
        }
    }
}
