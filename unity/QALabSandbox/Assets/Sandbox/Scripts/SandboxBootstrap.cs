using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// Registers the sandbox's seed catalog with QA Lab. AfterAssembliesLoaded runs before QA Lab's own
    /// BeforeSceneLoad bootstrap, so run.json (seeds_enabled) and labels.json already know the seeds.
    /// </summary>
    public static class SandboxBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void RegisterCatalog()
        {
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
