using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Editor-side switches with the same meaning as the command-line flags (spec 01). Create it with
    /// <c>Tools > QA Lab > Create or Select Settings</c>; it lives in a Resources folder so Play Mode can
    /// load it. Builds ignore it and read the command line only.
    /// </summary>
    [CreateAssetMenu(fileName = "QALabSettings", menuName = "QA Lab/Settings")]
    public sealed class QALabSettings : ScriptableObject
    {
        public const string ResourceName = "QALabSettings";

        [Tooltip("Start recording automatically when entering Play Mode.")]
        [SerializeField] private bool autoStartInPlayMode;
        [Tooltip("Parent folder for run folders. Relative paths are relative to the project folder. Empty = persistentDataPath/qalab/runs.")]
        [SerializeField] private string outDir = "";
        [SerializeField] private int seed;
        [SerializeField, Min(1f)] private float durationS = 120f;
        [Tooltip("Bot adapter (M4), or 'manual' for no bot.")]
        [SerializeField] private string adapter = "navmesh_explorer";
        [Tooltip("Scene to load first. Empty = the scene you pressed Play in.")]
        [SerializeField] private string scene = "";
        [SerializeField, Min(0f)] private float shotEveryS = 5f;
        [Tooltip("info | warning | error | exception | assert")]
        [SerializeField] private string minLevel = LogLevels.Warning;
        [Tooltip("Sandbox only: 'all' or e.g. 'SB01,SB06'.")]
        [SerializeField] private string seeds = "all";
        [SerializeField] private bool benchmark;
        [Tooltip("In the editor, leave Play Mode when the run ends.")]
        [SerializeField] private bool quitOnEnd;

        public bool AutoStartInPlayMode => autoStartInPlayMode;

        /// <summary>The settings as flags, so the editor and players share one parser.</summary>
        public List<string> ToArgs(string projectFolder)
        {
            var args = new List<string> { "-qalab" };
            if (!string.IsNullOrWhiteSpace(outDir))
            {
                var path = System.IO.Path.IsPathRooted(outDir) ? outDir : System.IO.Path.Combine(projectFolder, outDir);
                args.Add("-qalabOut");
                args.Add(System.IO.Path.GetFullPath(path));
            }
            Add(args, "-qalabSeed", seed.ToString(CultureInfo.InvariantCulture));
            Add(args, "-qalabDuration", durationS.ToString(CultureInfo.InvariantCulture));
            Add(args, "-qalabAdapter", adapter);
            if (!string.IsNullOrWhiteSpace(scene))
            {
                Add(args, "-qalabScene", scene);
            }
            Add(args, "-qalabShotEvery", shotEveryS.ToString(CultureInfo.InvariantCulture));
            Add(args, "-qalabMinLevel", minLevel);
            Add(args, "-qalabSeeds", string.IsNullOrWhiteSpace(seeds) ? "all" : seeds);
            if (benchmark) args.Add("-qalabBenchmark");
            if (quitOnEnd) args.Add("-qalabQuitOnEnd");
            return args;
        }

        private static void Add(List<string> args, string flag, string value)
        {
            args.Add(flag);
            args.Add(value);
        }
    }
}
