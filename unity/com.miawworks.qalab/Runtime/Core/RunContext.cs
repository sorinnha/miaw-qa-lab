using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Owns <c>run.json</c>: fills it from Unity's Application/SystemInfo, writes it at the start
    /// (without <c>ended_at</c>) and rewrites it on a clean end. A run that dies keeps the first version,
    /// which is how triage spots a possible crash.
    /// </summary>
    public sealed class RunContext
    {
        private readonly RunInfo _info;
        private readonly string _path;
        private readonly IClock _clock;

        public RunContext(QALabOptions options, string runId, string runDir, IClock clock, IEnumerable<string> seedsEnabled)
        {
            _clock = clock;
            _path = Path.Combine(runDir, "run.json");
            _info = new RunInfo
            {
                RunId = runId,
                Project = Application.productName,
                QALabVersion = QALab.Version,
                Build = new BuildInfo
                {
                    Version = Application.version,
                    Platform = PlatformName(),
                    Unity = Application.unityVersion,
                    GitSha = Application.isEditor ? null : BuildStamp.ReadGitSha(Application.streamingAssetsPath),
                    Development = Debug.isDebugBuild,
                },
                Mode = Application.isEditor ? "editor_playmode" : "player",
                Adapter = options.Adapter,
                Seed = options.Seed,
                StartedAt = Timestamps.Iso(clock.UtcNow),
                Machine = new MachineInfo
                {
                    Os = SystemInfo.operatingSystem,
                    Cpu = SystemInfo.processorType,
                    Gpu = SystemInfo.graphicsDeviceName,
                    RamGb = Math.Round(SystemInfo.systemMemorySize / 1024.0, 1),
                },
                Benchmark = options.Benchmark,
                SeedsEnabled = new List<string>(seedsEnabled),
            };
        }

        public string RunId => _info.RunId;

        /// <summary>Remember a scene the run visited (listed in run.json → scenes).</summary>
        public void AddScene(string scene)
        {
            if (!string.IsNullOrEmpty(scene) && !_info.Scenes.Contains(scene))
            {
                _info.Scenes.Add(scene);
            }
        }

        public void WriteStart() => _info.WriteTo(_path);

        public void WriteEnd(string exitReason, int exitCode)
        {
            _info.MarkEnded(_clock.UtcNow, _clock.Seconds, exitReason, exitCode);
            _info.WriteTo(_path);
        }

        private static string PlatformName()
        {
#if UNITY_EDITOR
            // Matches what a build would be: "StandaloneWindows64".
            return UnityEditor.EditorUserBuildSettings.activeBuildTarget.ToString();
#else
            return Application.platform.ToString();
#endif
        }
    }
}
