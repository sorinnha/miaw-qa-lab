// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;

namespace MiawWorks.QALab
{
    /// <summary>
    /// The <c>level</c> values of qalab.event/1 and the filter rules for log capture.
    /// Unity's <c>LogType</c> maps as Log→info, Warning→warning, Error→error, Exception→exception,
    /// Assert→assert (see <c>LogCapture.LevelOf</c>).
    /// </summary>
    public static class LogLevels
    {
        public const string Info = "info";
        public const string Warning = "warning";
        public const string Error = "error";
        public const string Exception = "exception";
        public const string Assert = "assert";

        /// <summary>Prefix of QA Lab's own log lines; they are never captured (no feedback loop).</summary>
        public const string OwnPrefix = "[QALab]";

        // Same order as the Python triage (info < warning < error < exception < assert).
        private static readonly string[] Order = { Info, Warning, Error, Exception, Assert };

        public static bool IsKnown(string level) => Array.IndexOf(Order, level) >= 0;

        /// <summary>True when <paramref name="level"/> is at or above <paramref name="minLevel"/>.</summary>
        public static bool Passes(string level, string minLevel)
        {
            var index = Array.IndexOf(Order, level);
            var min = Array.IndexOf(Order, minLevel);
            return index >= 0 && index >= (min < 0 ? 1 : min);
        }

        /// <summary>QA Lab's own messages start with <see cref="OwnPrefix"/>.</summary>
        public static bool IsOwnMessage(string message) =>
            message != null && message.StartsWith(OwnPrefix, StringComparison.Ordinal);
    }
}
