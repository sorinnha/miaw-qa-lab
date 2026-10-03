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
            foreach (var id in SandboxSeedCatalog.Stubbed())
            {
                Debug.LogWarning($"[QALab] catalog entry {id} is still a YOU WRITE stub; it won't appear in labels.json");
            }
        }
    }
}
