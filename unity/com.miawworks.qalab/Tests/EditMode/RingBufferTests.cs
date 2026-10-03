using System;
using MiawWorks.QALab;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    public class RingBufferTests
    {
        [Test]
        public void EmptyBufferReportsZero()
        {
            var buffer = new RingBuffer(4);
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(0f, buffer.Average());
            Assert.AreEqual(0f, buffer.Percentile(0.95f));
        }

        [Test]
        public void P95IsNearestRank()
        {
            var buffer = new RingBuffer(20);
            for (var i = 1; i <= 20; i++) buffer.Add(i);   // 1..20
            Assert.AreEqual(19f, buffer.Percentile(0.95f), "ceil(0.95 × 20) = 19th value");
            Assert.AreEqual(10.5f, buffer.Average());
            Assert.AreEqual(20f, buffer.Percentile(1f));
            Assert.AreEqual(10f, buffer.Percentile(0.5f));
        }

        [Test]
        public void P95Of100Values()
        {
            var buffer = new RingBuffer(100);
            for (var i = 100; i >= 1; i--) buffer.Add(i);   // insertion order doesn't matter
            Assert.AreEqual(95f, buffer.Percentile(0.95f));
        }

        [Test]
        public void OldestValuesAreOverwritten()
        {
            var buffer = new RingBuffer(3);
            foreach (var v in new[] { 100f, 1f, 2f, 3f }) buffer.Add(v);
            Assert.AreEqual(3, buffer.Count);
            Assert.AreEqual(2f, buffer.Average());
            Assert.AreEqual(3f, buffer.Percentile(0.95f));
            buffer.Clear();
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void PercentileDoesNotReorderTheBuffer()
        {
            var buffer = new RingBuffer(3);
            foreach (var v in new[] { 3f, 1f, 2f }) buffer.Add(v);
            buffer.Percentile(0.5f);
            buffer.Add(10f);   // overwrites the oldest (3), not the smallest
            Assert.AreEqual(13f / 3f, buffer.Average(), 1e-5);
        }

        [Test]
        public void BadArgumentsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer(0));
            var buffer = new RingBuffer(2);
            buffer.Add(1f);
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Percentile(0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => buffer.Percentile(1.5f));
        }
    }
}
