using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// Registers the sandbox's seed catalog and bot settings with QA Lab. AfterAssembliesLoaded runs
    /// before QA Lab's own BeforeSceneLoad bootstrap, so run.json (seeds_enabled) and labels.json already
    /// know the seeds.
    /// </summary>
    public static class SandboxBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterCatalog()
        {
            // A menu crawl stays in the menus: Play would leave Sandbox_Menu for the level, where there is
            // nothing to click. The level is the NavMesh explorer's job (D-026).
            BotAdapterRegistry.Register(UICrawlerAdapter.AdapterName, () => new UICrawlerAdapter(new[] { "Quit", "Exit", "Play" }));
            LabelRecorder.UseCatalog(SandboxSeedCatalog.All());
            var stubbed = SandboxSeedCatalog.Stubbed().Count;
            if (stubbed > 0)
            {
                // Count only: seed ids never go into log text (player.log sits in the run folder).
                Debug.LogWarning($"[QALab] {stubbed} SandboxSeedCatalog entry/entries are still YOU WRITE stubs and won't appear in labels.json");
            }
        }
    }
}
