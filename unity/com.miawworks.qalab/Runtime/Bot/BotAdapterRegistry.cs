using System;
using System.Collections.Generic;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Where bot adapters are found by name (<c>-qalabAdapter &lt;name&gt;</c>). Games register their
    /// own adapter once, before the first scene loads:
    /// <code>
    /// [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    /// static void RegisterAdapter() => BotAdapterRegistry.Register("my_game", () => new MyGameAdapter());
    /// </code>
    /// Names ignore case; registering a name again replaces it.
    /// </summary>
    public static class BotAdapterRegistry
    {
        private static readonly NamedRegistry<IBotAdapter> Adapters = new NamedRegistry<IBotAdapter>();

        public static void Register(string name, Func<IBotAdapter> factory) => Adapters.Register(name, factory);

        public static bool IsRegistered(string name) => Adapters.IsRegistered(name);

        /// <summary>A new adapter, or null for an unknown name.</summary>
        public static IBotAdapter Create(string name) => Adapters.Create(name);

        public static IReadOnlyList<string> Names => Adapters.Names;
    }
}
