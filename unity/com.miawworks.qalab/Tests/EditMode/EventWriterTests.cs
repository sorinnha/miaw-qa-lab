using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    public class EventWriterTests
    {
        private static List<JObject> Lines(string text) =>
            text.Split('\n').Where(l => l.Length > 0).Select(l => (JObject)Json.Parse(l)).ToList();

        [Test]
        public void SeqStartsAtZeroAndCarriesTheMainThreadSnapshot()
        {
            var clock = new FakeClock { Seconds = 1.5 };
            var state = new FakeState { Frame = 90, Position = new[] { 1f, 0f, 2f } };
            var output = new StringWriter();
            using (var writer = new EventWriter("run-1", clock, state, output))
            {
                writer.Marker("run_start");
                clock.Seconds = 2.25;
                writer.Log("error", "boom", "Game.A.B () (at Assets/A.cs:3)");
                Assert.AreEqual(2, writer.Pending);
                Assert.AreEqual(2, writer.Drain());
                Assert.AreEqual(2, writer.Written);
            }
            var lines = Lines(output.ToString());
            Assert.AreEqual(0L, (long)lines[0]["seq"]);
            Assert.AreEqual(1L, (long)lines[1]["seq"]);
            Assert.AreEqual(2.25, (double)lines[1]["t"]);
            Assert.AreEqual("Sandbox_Level01", (string)lines[1]["scene"]);
            Assert.AreEqual(90L, (long)lines[1]["frame"]);
            Assert.AreEqual("2026-10-05T10:30:00.000Z", (string)lines[1]["ts"]);
            CollectionAssert.AreEqual(new[] { 1.0, 0.0, 2.0 }, lines[1]["pos"].ToObject<double[]>());
        }

        [Test]
        public void EmptyStackBecomesNullAndNullPositionIsWritten()
        {
            var output = new StringWriter();
            using (var writer = new EventWriter("r", new FakeClock(), new FakeState(), output))
            {
                writer.Log("warning", "w", "");
            }
            var line = Lines(output.ToString())[0];
            Assert.AreEqual(JTokenType.Null, line["stack"].Type);
            Assert.AreEqual(JTokenType.Null, line["pos"].Type);
        }

        [Test]
        public void DrainSortsABatchThatWasQueuedOutOfOrder()
        {
            // Producer A takes seq 0, then pauses (inside the Frame read) before it enqueues. Producer B
            // takes seq 1 and enqueues. So the queue holds [1, 0]; the drained file must say [0, 1].
            var paused = new ManualResetEventSlim(false);
            var resume = new ManualResetEventSlim(false);
            var first = 1;
            var state = new FakeState
            {
                OnFrameRead = () =>
                {
                    if (Interlocked.Exchange(ref first, 0) == 1)
                    {
                        paused.Set();
                        resume.Wait(TimeSpan.FromSeconds(10));
                    }
                },
            };
            var output = new StringWriter();
            var writer = new EventWriter("r", new FakeClock(), state, output);
            var producerA = Task.Run(() => writer.Log("error", "from A", null));
            Assert.IsTrue(paused.Wait(TimeSpan.FromSeconds(10)), "producer A reached the pause");
            writer.Log("error", "from B", null);
            resume.Set();
            producerA.Wait(TimeSpan.FromSeconds(10));
            Assert.AreEqual(2, writer.Drain());
            writer.Dispose();

            var lines = Lines(output.ToString());
            CollectionAssert.AreEqual(new[] { 0L, 1L }, lines.Select(l => (long)l["seq"]));
            CollectionAssert.AreEqual(new[] { "from A", "from B" }, lines.Select(l => (string)l["message"]));
        }

        [Test]
        public void ManyThreadsGetUniqueGaplessSeqs()
        {
            // Deterministic interleaving (no timing assumptions): every producer enqueues half its
            // events and waits; the "main thread" drains exactly that half while all producers are
            // alive, releases them, and keeps draining while they enqueue the second half.
            const int threads = 8, perThread = 2000, half = perThread / 2;
            var output = new StringWriter();
            var writer = new EventWriter("r", new FakeClock(), new FakeState(), output);
            var halfwayDone = new CountdownEvent(threads);
            var release = new ManualResetEventSlim(false);
            var producers = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
            {
                for (var i = 0; i < half; i++) writer.Log("error", "x", null);
                halfwayDone.Signal();
                release.Wait(TimeSpan.FromSeconds(30));
                for (var i = half; i < perThread; i++) writer.Log("error", "x", null);
            })).ToArray();

            Assert.IsTrue(halfwayDone.Wait(TimeSpan.FromSeconds(30)), "producers reached halfway");
            Assert.AreEqual(threads * half, writer.Drain(), "a drain while every producer is alive");
            release.Set();
            while (!Task.WaitAll(producers, 1))
            {
                writer.Drain();   // drains race with the producers' second half
            }
            writer.Dispose();

            var lines = output.ToString().Split('\n').Where(l => l.Length > 0).ToList();
            Assert.AreEqual(threads * perThread, lines.Count);
            var seqs = lines.Select(l => (long)Json.Parse(l)["seq"]).ToList();
            CollectionAssert.AreEquivalent(Enumerable.Range(0, threads * perThread).Select(i => (long)i), seqs);
            Assert.AreEqual(threads * perThread, (int)writer.Written);
        }

        [Test]
        public void DisposeFlushesAndLaterEventsAreDropped()
        {
            var output = new StringWriter();
            var writer = new EventWriter("r", new FakeClock(), new FakeState(), output);
            writer.Marker("run_start");
            writer.Dispose();
            writer.Marker("run_end");
            Assert.AreEqual(0, writer.Drain());
            writer.Dispose();   // second dispose is a no-op
            Assert.AreEqual(1, Lines(output.ToString()).Count);
        }

        [Test]
        public void ToFileWritesUtf8WithoutBomAndLf()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qalab-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "events.jsonl");
                using (var writer = EventWriter.ToFile("r", new FakeClock(), new FakeState(), path))
                {
                    writer.Log("warning", "Footstep audio clip missing for surface 'Métal'", null);
                }
                var bytes = File.ReadAllBytes(path);
                Assert.AreNotEqual(0xEF, bytes[0], "no UTF-8 BOM");
                CollectionAssert.DoesNotContain(bytes, (byte)'\r');
                Assert.AreEqual((byte)'\n', bytes[bytes.Length - 1]);
                StringAssert.Contains("Métal", File.ReadAllText(path));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
