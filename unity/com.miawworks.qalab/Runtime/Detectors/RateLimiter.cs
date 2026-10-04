// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Spec 01 detector rate limit: the same detector in the same 4 m ground cell is reported at most once
    /// per 10 s. A player stuck against one wall makes one event every 10 s, not one per frame, while a
    /// second problem 4 m away is still reported. Cells use x and z only (the ground plane), the same
    /// cells triage uses to group detector events. Main thread only.
    /// </summary>
    public sealed class RateLimiter
    {
        public const float DefaultCellM = 4f;
        public const double DefaultWindowS = 10.0;

        private readonly Dictionary<(string Detector, int X, int Z), double> _last =
            new Dictionary<(string Detector, int X, int Z), double>();

        public RateLimiter(float cellM = DefaultCellM, double windowS = DefaultWindowS)
        {
            if (!(cellM > 0f)) throw new ArgumentOutOfRangeException(nameof(cellM), "cell size must be > 0");
            if (windowS < 0.0) throw new ArgumentOutOfRangeException(nameof(windowS), "window must be >= 0");
            CellM = cellM;
            WindowS = windowS;
        }

        public float CellM { get; }

        public double WindowS { get; }

        /// <summary>
        /// The ground cell of <paramref name="pos"/> (x, y, z): floor(x / cell), floor(z / cell). Events
        /// without a position share one cell, so they are still limited per detector.
        /// </summary>
        public (int X, int Z) Cell(float[] pos)
        {
            if (pos == null || pos.Length < 3) return (int.MinValue, int.MinValue);
            return ((int)Math.Floor(pos[0] / CellM), (int)Math.Floor(pos[2] / CellM));
        }

        /// <summary>
        /// True if <paramref name="detector"/> may report at run time <paramref name="t"/> from
        /// <paramref name="pos"/>, and remembers it. A refused report doesn't restart the window.
        /// </summary>
        public bool Allow(string detector, float[] pos, double t)
        {
            var cell = Cell(pos);
            var key = (detector ?? string.Empty, cell.X, cell.Z);
            if (_last.TryGetValue(key, out var last) && t - last < WindowS)
            {
                return false;
            }
            _last[key] = t;
            return true;
        }

        /// <summary>Forget everything (a new run).</summary>
        public void Clear() => _last.Clear();
    }
}
