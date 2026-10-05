// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;

namespace MiawWorks.QALab
{
    /// <summary>
    /// The math behind the <c>stuck</c> detector (spec 01): the bot is asking the player to move, but the
    /// player moved less than <see cref="MinDistanceM"/> on the ground plane (x, z) in the last
    /// <see cref="WindowS"/> seconds. Fed one sample per frame by <see cref="StuckDetector"/>.
    /// Plain C# so EditMode tests (tools/cs-check too) cover it without a scene.
    /// </summary>
    public sealed class StuckCalculator
    {
        public const double DefaultWindowS = 4.0;
        public const float DefaultMinDistanceM = 0.5f;

        public StuckCalculator(double windowS = DefaultWindowS, float minDistanceM = DefaultMinDistanceM)
        {
            if (!(windowS > 0.0)) throw new ArgumentOutOfRangeException(nameof(windowS), "window must be > 0");
            if (!(minDistanceM > 0f)) throw new ArgumentOutOfRangeException(nameof(minDistanceM), "distance must be > 0");
            WindowS = windowS;
            MinDistanceM = minDistanceM;
        }

        public double WindowS { get; }

        public float MinDistanceM { get; }

        // Samples newer than the anchor, oldest at the front (they arrive in time order).
        private readonly Queue<Sample> _recent = new Queue<Sample>();

        // The newest sample at least one window old, or null until the bot has moved for a window.
        private Sample? _anchor;

        /// <summary>
        /// Learning task (M4), written by Claude at Sora's request (D-030). How many samples are remembered
        /// right now. Tests use it to check that memory stays bounded: at 60 fps it never holds much more
        /// than one window of samples.
        /// </summary>
        public int SampleCount => _recent.Count + (_anchor.HasValue ? 1 : 0);

        /// <summary>
        /// Learning task (M4), written by Claude at Sora's request (D-030). Add the sample for run time
        /// <paramref name="t"/> (seconds, never decreasing) and return true if the player is stuck at
        /// <paramref name="t"/>.
        /// <list type="number">
        /// <item>If <paramref name="moving"/> is false (the bot isn't asking the player to move), forget
        /// every sample and return false: standing still on purpose is not stuck.</item>
        /// <item>Otherwise remember (t, x, z).</item>
        /// <item>The anchor is the newest remembered sample whose time is &lt;= t − WindowS. If there is no
        /// anchor, the bot hasn't been moving for a whole window yet: return false.</item>
        /// <item>Forget the samples older than the anchor (they can never be an anchor again), so memory
        /// stays bounded.</item>
        /// <item>Return true if the ground distance from the anchor to (x, z), √(dx² + dz²), is less than
        /// MinDistanceM.</item>
        /// </list>
        /// y is not passed in: falling or climbing in place still counts as stuck.
        /// Hint: samples arrive in time order, so a <c>Queue</c> (or a <c>List</c> you trim from the front)
        /// keeps the oldest one at the front. Compare to the RingBuffer in Metrics/.
        /// </summary>
        public bool Add(double t, float x, float z, bool moving)
        {
            if (!moving)
            {
                Reset();
                return false;
            }

            _recent.Enqueue(new Sample(t, x, z));
            // Every sample at least one window old moves out of the queue; the last one to leave is the
            // newest such sample, so it becomes the anchor and the older ones are dropped for good.
            double cutoff = t - WindowS;
            while (_recent.Count > 0 && _recent.Peek().T <= cutoff) _anchor = _recent.Dequeue();
            if (!_anchor.HasValue) return false;

            double dx = (double)x - _anchor.Value.X;   // in double, so 0.5 - 0 is exactly 0.5
            double dz = (double)z - _anchor.Value.Z;
            return Math.Sqrt(dx * dx + dz * dz) < MinDistanceM;
        }

        /// <summary>
        /// Learning task (M4), written by Claude at Sora's request (D-030). Forget every sample (after a
        /// report, or when the run state changes).
        /// </summary>
        public void Reset()
        {
            _recent.Clear();
            _anchor = null;
        }

        private readonly struct Sample
        {
            public Sample(double t, float x, float z)
            {
                T = t;
                X = x;
                Z = z;
            }

            public double T { get; }

            public float X { get; }

            public float Z { get; }
        }
    }
}
