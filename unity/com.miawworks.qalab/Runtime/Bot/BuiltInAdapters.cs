namespace MiawWorks.QALab
{
    /// <summary>
    /// Registers the built-in bots. <see cref="BotAdapterRegistry"/> calls this from its static
    /// constructor, so the built-ins exist before any game code registers (and can replace) them.
    /// </summary>
    internal static class BuiltInAdapters
    {
        public static void RegisterAll()
        {
#if QALAB_AI
            BotAdapterRegistry.Register(NavMeshExplorerAdapter.AdapterName, () => new NavMeshExplorerAdapter());
#endif
#if QALAB_UGUI
            BotAdapterRegistry.Register(UICrawlerAdapter.AdapterName, () => new UICrawlerAdapter());
#endif
        }
    }
}
