// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Diagnostics;
using System.Globalization;

namespace MiawWorks.QALab
{
    /// <summary>Seconds since run start plus wall-clock time. Implementations must be thread-safe.</summary>
    public interface IClock
    {
        /// <summary>Seconds since the run started, rounded to milliseconds.</summary>
        double Seconds { get; }
        DateTime UtcNow { get; }
    }

    /// <summary>
    /// <see cref="Stopwatch"/>-based clock. Unity's <c>Time.time</c> may only be read on the main
    /// thread, but log callbacks arrive on any thread, so <c>t</c> comes from here instead.
    /// </summary>
    public sealed class StopwatchClock : IClock
    {
        private readonly Stopwatch _watch = Stopwatch.StartNew();

        public double Seconds => Math.Round(_watch.Elapsed.TotalSeconds, 3);
        public DateTime UtcNow => DateTime.UtcNow;
    }

    public static class Timestamps
    {
        /// <summary>ISO 8601 UTC with milliseconds: <c>2026-10-05T10:30:08.210Z</c>.</summary>
        public static string Iso(DateTime utc) =>
            utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
    }
}
