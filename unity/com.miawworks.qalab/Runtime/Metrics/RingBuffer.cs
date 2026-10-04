// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Fixed-size buffer of the last N frame times. Add/Average/Percentile never allocate after
    /// construction (the sort uses a preallocated scratch array), so it is safe to use every frame.
    /// </summary>
    public sealed class RingBuffer
    {
        private readonly float[] _items;
        private readonly float[] _scratch;
        private int _next;
        private int _count;

        public RingBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "capacity must be > 0");
            }
            _items = new float[capacity];
            _scratch = new float[capacity];
        }

        public int Capacity => _items.Length;
        public int Count => _count;

        /// <summary>Add a value; the oldest one is overwritten when full.</summary>
        public void Add(float value)
        {
            _items[_next] = value;
            _next = (_next + 1) % _items.Length;
            if (_count < _items.Length)
            {
                _count++;
            }
        }

        public void Clear()
        {
            _next = 0;
            _count = 0;
        }

        /// <summary>Mean of the stored values, 0 when empty.</summary>
        public float Average()
        {
            if (_count == 0)
            {
                return 0f;
            }
            double sum = 0;
            for (var i = 0; i < _count; i++)
            {
                sum += _items[i];
            }
            return (float)(sum / _count);
        }

        /// <summary>
        /// Nearest-rank percentile (<paramref name="p"/> in 0..1): sort, then take the value at
        /// <c>ceil(p × n) − 1</c>. p = 0.95 over 20 values is the 19th smallest. 0 when empty.
        /// </summary>
        public float Percentile(float p)
        {
            if (_count == 0)
            {
                return 0f;
            }
            if (p <= 0f || p > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(p), "p must be in (0, 1]");
            }
            Array.Copy(_items, _scratch, _count);
            Array.Sort(_scratch, 0, _count);
            // Round first: 0.95f * 20 is 18.9999997 in floating point, and we want rank 19.
            var rank = (int)Math.Ceiling(Math.Round(p * (double)_count, 6)) - 1;
            return _scratch[Math.Max(0, Math.Min(rank, _count - 1))];
        }
    }
}
