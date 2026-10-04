// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;

namespace MiawWorks.QALab
{
    /// <summary>
    /// The bot's only source of randomness (spec 01, "Determinism"): <see cref="System.Random"/> seeded
    /// with <c>-qalabSeed</c>, so the same seed makes the same decisions. Bot code never uses
    /// <c>UnityEngine.Random</c>: the game shares its state, so any game call would shift the bot's choices.
    /// Physics and frame timing still differ between runs; the action log is the repro record.
    /// </summary>
    public sealed class SeededRandom
    {
        private readonly Random _random;

        public SeededRandom(int seed)
        {
            Seed = seed;
            _random = new Random(seed);
        }

        public int Seed { get; }

        /// <summary>An int in [0, <paramref name="maxExclusive"/>).</summary>
        public int Next(int maxExclusive) => _random.Next(maxExclusive);

        /// <summary>An int in [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
        public int Range(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);

        /// <summary>A float in [<paramref name="min"/>, <paramref name="max"/>).</summary>
        public float Range(float min, float max) => min + (float)_random.NextDouble() * (max - min);

        /// <summary>True with probability <paramref name="p"/> (0 = never, 1 = always).</summary>
        public bool Chance(double p) => _random.NextDouble() < p;

        /// <summary>One item, uniformly. Throws on an empty list: the caller decides what "nothing to do" means.</summary>
        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0)
            {
                throw new ArgumentException("nothing to pick from", nameof(items));
            }
            return items[_random.Next(items.Count)];
        }
    }
}
