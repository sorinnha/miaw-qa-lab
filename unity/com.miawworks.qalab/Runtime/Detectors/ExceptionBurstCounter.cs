// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Counts exception events in a sliding time window (spec 01 <c>exception_burst</c>: more than 20 within
    /// 1 s). Exceptions are logged on any thread, so the host counts them as they are written and hands
    /// this class one total per frame; the window itself is main-thread only.
    /// </summary>
    public sealed class ExceptionBurstCounter
    {
        public const int DefaultThreshold = 20;
        public const double DefaultWindowS = 1.0;

        private readonly Queue<(double T, int Count)> _window = new Queue<(double T, int Count)>();
        private int _total;

        public ExceptionBurstCounter(int threshold = DefaultThreshold, double windowS = DefaultWindowS)
        {
            if (threshold < 0) throw new ArgumentOutOfRangeException(nameof(threshold));
            if (!(windowS > 0.0)) throw new ArgumentOutOfRangeException(nameof(windowS));
            Threshold = threshold;
            WindowS = windowS;
        }

        public int Threshold { get; }

        public double WindowS { get; }

        /// <summary>Exceptions within the window ending at the last <see cref="Add"/>.</summary>
        public int Total => _total;

        /// <summary>True when the window holds more than <see cref="Threshold"/> exceptions.</summary>
        public bool IsBurst => _total > Threshold;

        /// <summary>
        /// Record <paramref name="count"/> exceptions seen at run time <paramref name="t"/> (times never go
        /// backwards) and drop those older than the window, so the window is (t − WindowS, t]. Returns
        /// <see cref="Total"/>.
        /// </summary>
        public int Add(double t, int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > 0)
            {
                _window.Enqueue((t, count));
                _total += count;
            }
            while (_window.Count > 0 && _window.Peek().T <= t - WindowS)
            {
                _total -= _window.Dequeue().Count;
            }
            return _total;
        }

        /// <summary>Empty the window, e.g. after reporting a burst, so the next one needs new exceptions.</summary>
        public void Reset()
        {
            _window.Clear();
            _total = 0;
        }
    }
}
