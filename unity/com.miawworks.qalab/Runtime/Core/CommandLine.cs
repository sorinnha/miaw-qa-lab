// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MiawWorks.QALab
{
    /// <summary>Which sandbox seeded bugs are enabled (<c>-qalabSeeds all</c> or <c>SB01,SB06</c>).</summary>
    public sealed class SeedSelection
    {
        // "sb01" → "SB01" after ToUpperInvariant; "SB1" or "SBX1" do not match.
        private static readonly Regex SeedId = new Regex("^SB[0-9]{2}$", RegexOptions.Compiled);

        private readonly HashSet<string> _ids;

        private SeedSelection(HashSet<string> ids) { _ids = ids; }

        /// <summary>Every seed is enabled (the default).</summary>
        public static SeedSelection All { get; } = new SeedSelection(null);

        public bool IsAll => _ids == null;

        /// <summary>The explicit ids, sorted; empty when <see cref="IsAll"/>.</summary>
        public IReadOnlyList<string> Ids
        {
            get
            {
                var list = _ids == null ? new List<string>() : new List<string>(_ids);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        public bool IsEnabled(string bugId) => _ids == null || _ids.Contains(bugId);

        /// <summary>
        /// run.json <c>seeds_enabled</c>, sorted: for <c>all</c>, every id in the game's catalog; for a
        /// list, exactly the listed ids, including ids the catalog doesn't have (yet).
        /// </summary>
        public List<string> EnabledIds(IEnumerable<string> catalogIds)
        {
            var ids = new List<string>(_ids ?? new HashSet<string>(catalogIds, StringComparer.Ordinal));
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>Parse <c>all</c> or a comma list like <c>SB01, sb06</c>. Returns null on a bad id.</summary>
        public static SeedSelection Parse(string text, out string error)
        {
            error = null;
            if (string.Equals(text?.Trim(), "all", StringComparison.OrdinalIgnoreCase))
            {
                return All;
            }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var part in (text ?? string.Empty).Split(','))
            {
                var id = part.Trim().ToUpperInvariant();
                if (id.Length == 0)
                {
                    continue;
                }
                if (!SeedId.IsMatch(id))
                {
                    error = $"bad seed id '{part.Trim()}' (expected SB01..SB99 or 'all')";
                    return null;
                }
                ids.Add(id);
            }
            if (ids.Count == 0)
            {
                error = "empty seed list (use 'all' or e.g. 'SB01,SB06')";
                return null;
            }
            return new SeedSelection(ids);
        }
    }

    /// <summary>Everything the command-line flags control (spec 01, "Activation and configuration").</summary>
    public sealed class QALabOptions
    {
        /// <summary><c>-qalab</c>: record a run.</summary>
        public bool Enabled;
        /// <summary><c>-qalabOut</c>: parent folder for run folders; null = persistentDataPath/qalab/runs.</summary>
        public string OutDir;
        /// <summary><c>-qalabSeed</c>: seed for the bot's random numbers.</summary>
        public int Seed;
        /// <summary><c>-qalabDuration</c>: seconds until the run ends.</summary>
        public float DurationS = 120f;
        /// <summary><c>-qalabAdapter</c>: bot adapter name, or <c>manual</c> for no bot.</summary>
        public string Adapter = "navmesh_explorer";
        /// <summary><c>-qalabScene</c>: scene to load first; null means the active scene.</summary>
        public string Scene;
        /// <summary><c>-qalabShotEvery</c>: seconds between periodic screenshots (0 = off, M4).</summary>
        public float ShotEveryS = 5f;
        /// <summary><c>-qalabMinLevel</c>: lowest log level captured.</summary>
        public string MinLevel = LogLevels.Warning;
        /// <summary><c>-qalabSeeds</c>: sandbox seeded bugs to enable.</summary>
        public SeedSelection Seeds = SeedSelection.All;
        /// <summary><c>-qalabBenchmark</c>: write labels.json.</summary>
        public bool Benchmark;
        /// <summary><c>-qalabQuitOnEnd</c>: quit (or leave Play Mode) when the run ends.</summary>
        public bool QuitOnEnd;
    }

    /// <summary>
    /// Parses QA Lab's <c>-qalab*</c> flags. Other arguments (Unity's own) are ignored. A bad value
    /// is reported in <c>errors</c> and the default is kept, so a typo never stops a playtest.
    /// </summary>
    public static class CommandLine
    {
        // "navmesh_explorer" ok; "NavMesh Explorer" or "crimson-tactics" rejected.
        private static readonly Regex AdapterName = new Regex("^[a-z0-9_]+$", RegexOptions.Compiled);

        /// <summary>The one rule for adapter names (snake_case), shared by <c>-qalabAdapter</c> and BotAdapterRegistry.</summary>
        public static bool IsAdapterName(string name) => name != null && AdapterName.IsMatch(name);

        public static QALabOptions Parse(IList<string> args, List<string> errors)
        {
            var options = new QALabOptions();
            for (var i = 0; i < args.Count; i++)
            {
                var flag = args[i];
                if (!flag.StartsWith("-qalab", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                switch (flag.ToLowerInvariant())
                {
                    case "-qalab": options.Enabled = true; break;
                    case "-qalabbenchmark": options.Benchmark = true; break;
                    case "-qalabquitonend": options.QuitOnEnd = true; break;
                    case "-qalabout":
                        if (TakeValue(args, ref i, flag, errors, out var outDir)) options.OutDir = outDir;
                        break;
                    case "-qalabseed":
                        if (TakeValue(args, ref i, flag, errors, out var seedText))
                        {
                            if (int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed)) options.Seed = seed;
                            else errors.Add($"{flag}: '{seedText}' is not an integer");
                        }
                        break;
                    case "-qalabduration":
                        if (TakeValue(args, ref i, flag, errors, out var durationText))
                        {
                            if (TryPositive(durationText, allowZero: false, out var duration)) options.DurationS = duration;
                            else errors.Add($"{flag}: '{durationText}' must be a number > 0");
                        }
                        break;
                    case "-qalabshotevery":
                        if (TakeValue(args, ref i, flag, errors, out var shotText))
                        {
                            if (TryPositive(shotText, allowZero: true, out var every)) options.ShotEveryS = every;
                            else errors.Add($"{flag}: '{shotText}' must be a number >= 0");
                        }
                        break;
                    case "-qalabadapter":
                        if (TakeValue(args, ref i, flag, errors, out var adapter))
                        {
                            if (IsAdapterName(adapter)) options.Adapter = adapter;
                            else errors.Add($"{flag}: '{adapter}' must be snake_case");
                        }
                        break;
                    case "-qalabscene":
                        if (TakeValue(args, ref i, flag, errors, out var scene)) options.Scene = scene;
                        break;
                    case "-qalabminlevel":
                        if (TakeValue(args, ref i, flag, errors, out var level))
                        {
                            var normalized = level.ToLowerInvariant();
                            if (LogLevels.IsKnown(normalized)) options.MinLevel = normalized;
                            else errors.Add($"{flag}: unknown level '{level}'");
                        }
                        break;
                    case "-qalabseeds":
                        if (TakeValue(args, ref i, flag, errors, out var seedsText))
                        {
                            var seeds = SeedSelection.Parse(seedsText, out var seedError);
                            if (seeds != null) options.Seeds = seeds;
                            else errors.Add($"{flag}: {seedError}");
                        }
                        break;
                    default:
                        errors.Add($"unknown flag {flag}");
                        break;
                }
            }
            return options;
        }

        /// <summary>The next argument as the flag's value; a following flag counts as missing.</summary>
        private static bool TakeValue(IList<string> args, ref int i, string flag, List<string> errors, out string value)
        {
            if (i + 1 >= args.Count || args[i + 1].StartsWith("-qalab", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"{flag}: missing value");
                value = null;
                return false;
            }
            i++;
            value = args[i];
            return true;
        }

        private static bool TryPositive(string text, bool allowZero, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                && !float.IsNaN(value) && !float.IsInfinity(value)
                && (allowZero ? value >= 0f : value > 0f);
        }
    }
}
