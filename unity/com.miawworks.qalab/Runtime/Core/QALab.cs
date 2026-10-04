using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Static entry point. With <c>-qalab</c> (or the settings asset's "Auto-start in Play Mode") it
    /// creates one <c>DontDestroyOnLoad</c> "QALab" object before the first scene loads, and that object
    /// records the run. Without it, everything here is inert and costs nothing.
    /// </summary>
    public static class QALab
    {
        public const string Version = "0.1.0";

        private static QALabHost _host;

        /// <summary>True while a run is being recorded.</summary>
        public static bool IsRunning => _host != null && _host.IsRecording;

        /// <summary>The parsed flags; defaults when QA Lab is off.</summary>
        public static QALabOptions Options { get; private set; } = new QALabOptions();

        /// <summary>The current run folder, or null.</summary>
        public static string RunDir => _host != null ? _host.RunDir : null;

        /// <summary>The registered player (events take their <c>pos</c> from it), or null.</summary>
        public static Transform Player { get; private set; }

        /// <summary>The registered player's mover, used by the M4 bot; null until registered.</summary>
        public static IBotMover Mover { get; private set; }

        /// <summary>Games call this once their player exists, so events carry the player's position.</summary>
        public static void RegisterPlayer(Transform player, IBotMover mover)
        {
            Player = player;
            Mover = mover;
        }

        /// <summary>
        /// Sandbox seeds ask this before misbehaving. Outside a run every seed is on, so the sandbox
        /// is buggy when played normally; during a run <c>-qalabSeeds</c> decides.
        /// </summary>
        public static bool IsSeedEnabled(string bugId) => !IsRunning || Options.Seeds.IsEnabled(bugId);

        /// <summary>End the run now (writes run_end, run.json with ended_at, labels.json).</summary>
        public static void EndRun(string exitReason = ExitReasons.TestFinished)
        {
            if (_host != null) _host.EndRun(exitReason);
        }

        // Domain reload can be disabled in the editor ("Enter Play Mode Options"), which keeps statics
        // alive between Play sessions. Reset them first thing.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _host = null;
            Options = new QALabOptions();
            Player = null;
            Mover = null;
            LabelRecorder.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            var errors = new List<string>();
            var options = CommandLine.Parse(ReadArgs(), errors);
            foreach (var error in errors)
            {
                Debug.LogWarning("[QALab] " + error);
            }
            Options = options;
            if (!options.Enabled)
            {
                return;
            }
            LogCapture.ConfigureStackTraces();
            try
            {
                var runId = RunIds.Create(DateTime.UtcNow, options.Seed);
                var runDir = UniqueRunDir(OutDir(options), ref runId);
                var go = new GameObject("QALab");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<QALabHost>();
                _host.Begin(options, runId, runDir);
            }
            catch (Exception exc)
            {
                // A broken QA Lab must never break the game; report and stay off.
                Debug.LogError("[QALab] could not start: " + exc.Message);
                _host = null;
            }
        }

        /// <summary>Command line in players; the settings asset (if auto-start is on) in the editor.</summary>
        private static IList<string> ReadArgs()
        {
            var args = new List<string>(Environment.GetCommandLineArgs());
#if UNITY_EDITOR
            // The Test Runner enters Play Mode too (batch "-runTests" or its temporary InitTestScene);
            // auto-starting there would record the tests' deliberate errors as a game run.
            var underTestRunner = args.Exists(a => string.Equals(a, "-runTests", StringComparison.OrdinalIgnoreCase))
                || SceneManager.GetActiveScene().name.StartsWith("InitTestScene", StringComparison.Ordinal);
            var settings = Resources.Load<QALabSettings>(QALabSettings.ResourceName);
            if (settings != null && settings.AutoStartInPlayMode && !underTestRunner)
            {
                return settings.ToArgs(ProjectFolder());
            }
#endif
            return args;
        }

        /// <summary>The parent folder for run folders (spec 01: default persistentDataPath/qalab/runs).</summary>
        private static string OutDir(QALabOptions options)
        {
            if (string.IsNullOrEmpty(options.OutDir))
            {
                return Path.Combine(Application.persistentDataPath, "qalab", "runs");
            }
            var baseDir = Application.isEditor ? ProjectFolder() : Environment.CurrentDirectory;
            return Path.GetFullPath(Path.IsPathRooted(options.OutDir) ? options.OutDir : Path.Combine(baseDir, options.OutDir));
        }

        /// <summary>
        /// Two runs in the same second with the same seed get <c>_2</c>, <c>_3</c>, ... appended (still a
        /// valid run_id: <c>^[A-Za-z0-9_.-]+$</c>), so a second run never overwrites the first (D-022).
        /// </summary>
        private static string UniqueRunDir(string outDir, ref string runId)
        {
            var baseId = runId;
            var dir = Path.Combine(outDir, runId);
            for (var n = 2; Directory.Exists(dir); n++)
            {
                runId = baseId + "_" + n;
                dir = Path.Combine(outDir, runId);
            }
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static string ProjectFolder() => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}
