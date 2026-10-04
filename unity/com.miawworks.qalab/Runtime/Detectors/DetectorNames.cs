// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Text.RegularExpressions;

namespace MiawWorks.QALab
{
    /// <summary>Detector names (spec 00: snake_case). Games may add their own with the same rule.</summary>
    public static class DetectorNames
    {
        public const string Stuck = "stuck";
        public const string FellOutOfWorld = "fell_out_of_world";
        public const string PerfSpike = "perf_spike";
        public const string ExceptionBurst = "exception_burst";
        public const string Tunneling = "tunneling";

        /// <summary>The built-in detectors, in the order results.xml lists them.</summary>
        public static readonly string[] BuiltIn = { Stuck, FellOutOfWorld, PerfSpike, ExceptionBurst, Tunneling };

        // The event schema allows "name" or "name:label"; the ":label" form is Python's (visual:<label>).
        private static readonly Regex Valid = new Regex("^[a-z0-9_]+$", RegexOptions.CultureInvariant);

        public static bool IsValid(string name) => name != null && Valid.IsMatch(name);
    }

    /// <summary>
    /// Detector severity words (spec 00). They describe the detector signal; report severity (S1–S4) and
    /// priority are decided later by triage.
    /// </summary>
    public static class DetectorSeverity
    {
        public const string Blocker = "blocker";
        public const string Critical = "critical";
        public const string Major = "major";
        public const string Minor = "minor";
        public const string Trivial = "trivial";

        private static readonly string[] Order = { Blocker, Critical, Major, Minor, Trivial };

        public static bool IsKnown(string severity) => Array.IndexOf(Order, severity) >= 0;

        /// <summary>0 = blocker (worst) … 4 = trivial; -1 for an unknown word.</summary>
        public static int Rank(string severity) => Array.IndexOf(Order, severity);

        /// <summary>Blocker and critical make the run fail (exit code 1, spec 01).</summary>
        public static bool IsFatal(string severity) => severity == Blocker || severity == Critical;

        /// <summary>The worse of two severities; null counts as "none yet".</summary>
        public static string Worst(string a, string b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return Rank(a) <= Rank(b) ? a : b;
        }
    }
}
