// Engine-free: runs in Unity's Test Runner and in tools/cs-check.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    /// <summary>Spec 01 detectors: the rate limiter, the hub and the engine-free rules.</summary>
    public class DetectorTests
    {
        /// <summary>A hub writing to memory, with a settable clock and player position.</summary>
        private sealed class Rig
        {
            public readonly FakeClock Clock = new FakeClock();
            public readonly FakeState State = new FakeState { Position = new[] { 1f, 0f, 1f } };
            public readonly StringWriter Output = new StringWriter();
            public readonly List<string> Warnings = new List<string>();
            public readonly EventWriter Writer;
            public readonly DetectorHub Hub;
            public int ShotsRequested;

            public Rig(bool screenshots = true)
            {
                Writer = new EventWriter("r", Clock, State, Output);
                Func<string> shot = null;
                if (screenshots) shot = () => Shots.RelativePath(++ShotsRequested);
                Hub = new DetectorHub(Writer, Clock, State, new RateLimiter(), shot, Warnings.Add);
            }

            public List<JObject> Events()
            {
                Writer.Drain();
                return Output.ToString().Split('\n').Where(l => l.Length > 0).Select(l => (JObject)Json.Parse(l)).ToList();
            }

            public DetectorFrame Frame(float frameMs = 16f, bool capture = false, float[] pos = null, bool moving = false, int exceptions = 0) =>
                new DetectorFrame(Clock.Seconds, 1, frameMs, capture, pos ?? State.Position, moving, exceptions);
        }

        /// <summary>Records what a detector reported, without a hub.</summary>
        private sealed class Recorder : IDetectorReporter
        {
            public readonly List<(string Detector, string Severity, JObject Details)> Reports = new List<(string, string, JObject)>();

            public QAEvent Report(string detector, string severity, JObject details = null, float[] pos = null)
            {
                Reports.Add((detector, severity, details));
                return new QAEvent();
            }
        }

        private sealed class Throwing : IDetector
        {
            private readonly Exception _error;
            public int Ticks;
            public Throwing(string name, Exception error) { Name = name; _error = error; }
            public string Name { get; }
            public void Tick(in DetectorFrame frame, IDetectorReporter reporter) { Ticks++; throw _error; }
        }

        // ---- severity and names -----------------------------------------------------------------------

        [Test]
        public void SeverityRanksAndFatality()
        {
            Assert.AreEqual(DetectorSeverity.Critical, DetectorSeverity.Worst(DetectorSeverity.Minor, DetectorSeverity.Critical));
            Assert.AreEqual(DetectorSeverity.Major, DetectorSeverity.Worst(null, DetectorSeverity.Major));
            Assert.IsTrue(DetectorSeverity.IsFatal(DetectorSeverity.Blocker));
            Assert.IsTrue(DetectorSeverity.IsFatal(DetectorSeverity.Critical));
            Assert.IsFalse(DetectorSeverity.IsFatal(DetectorSeverity.Major));
            Assert.IsFalse(DetectorSeverity.IsKnown("S2"), "report severities are triage's, not detector words");
            CollectionAssert.AreEqual(new[] { "stuck", "fell_out_of_world", "perf_spike", "exception_burst", "tunneling" }, DetectorNames.BuiltIn);
            Assert.IsFalse(DetectorNames.IsValid("visual:black_screen"), "the visual: prefix is added by Python only");
        }

        // ---- rate limiter -----------------------------------------------------------------------------

        [Test]
        public void SameDetectorSameCellIsReportedOncePerWindow()
        {
            var limiter = new RateLimiter();
            var pos = new[] { 5f, 0f, 5f };
            Assert.IsTrue(limiter.Allow("stuck", pos, 0.0));
            Assert.IsFalse(limiter.Allow("stuck", new[] { 7.9f, 3f, 4.1f }, 9.99), "same 4 m cell (y ignored)");
            Assert.IsTrue(limiter.Allow("stuck", pos, 10.0), "the window is 10 s");
            Assert.IsFalse(limiter.Allow("stuck", pos, 19.5));
        }

        [Test]
        public void OtherCellsAndOtherDetectorsAreIndependent()
        {
            var limiter = new RateLimiter();
            Assert.IsTrue(limiter.Allow("stuck", new[] { 3.9f, 0f, 0f }, 1.0));
            Assert.IsTrue(limiter.Allow("stuck", new[] { 4.0f, 0f, 0f }, 1.0), "x = 4 is the next cell");
            Assert.IsTrue(limiter.Allow("perf_spike", new[] { 3.9f, 0f, 0f }, 1.0));
            Assert.IsTrue(limiter.Allow("stuck", new[] { -0.1f, 0f, 0f }, 1.0), "floor, not truncation: -0.1 is cell -1");
        }

        [Test]
        public void RefusedReportsDoNotRestartTheWindowAndNoPositionSharesOneCell()
        {
            var limiter = new RateLimiter(cellM: 4f, windowS: 10.0);
            Assert.IsTrue(limiter.Allow("perf_spike", null, 0.0));
            Assert.IsFalse(limiter.Allow("perf_spike", null, 6.0));
            Assert.IsTrue(limiter.Allow("perf_spike", null, 10.0), "6 s didn't push the window out");
            Assert.AreEqual((8, 0), limiter.Cell(new[] { 35.2f, -12f, 1.1f }), "same cells as triage (sample SB06)");
            Assert.Throws<ArgumentOutOfRangeException>(() => new RateLimiter(cellM: 0f));
        }

        // ---- hub --------------------------------------------------------------------------------------

        [Test]
        public void ReportWritesASchemaShapedEventWithAScreenshotAndCounts()
        {
            var rig = new Rig();
            rig.Clock.Seconds = 26.9;
            rig.State.Position = new[] { 35.2f, -12f, 1.1f };
            var e = rig.Hub.Report(DetectorNames.FellOutOfWorld, DetectorSeverity.Critical, new JObject { ["kill_plane_y"] = -5.5 });

            Assert.IsNotNull(e);
            var line = rig.Events().Single();
            Assert.AreEqual("detector", (string)line["kind"]);
            Assert.AreEqual("fell_out_of_world", (string)line["data"]["detector"]);
            Assert.AreEqual("critical", (string)line["data"]["severity"]);
            Assert.AreEqual(-5.5, (double)line["data"]["details"]["kill_plane_y"]);
            Assert.AreEqual("shots/000001.png", (string)line["data"]["screenshot"]);
            CollectionAssert.AreEqual(new[] { 35.2, -12.0, 1.1 }, line["pos"].ToObject<double[]>().Select(v => Math.Round(v, 2)));
            Assert.AreEqual(1, rig.Hub.Count("fell_out_of_world"));
            Assert.AreEqual("critical", rig.Hub.WorstSeverity("fell_out_of_world"));
            Assert.IsTrue(rig.Hub.FatalFired);
        }

        [Test]
        public void RateLimitedReportsWriteNothingAndTakeNoScreenshot()
        {
            var rig = new Rig();
            Assert.IsNotNull(rig.Hub.Report("perf_spike", "minor"));
            rig.Clock.Seconds = 3;
            Assert.IsNull(rig.Hub.Report("perf_spike", "minor"));
            Assert.AreEqual(1, rig.Events().Count);
            Assert.AreEqual(1, rig.ShotsRequested);
            Assert.AreEqual(1, rig.Hub.Count("perf_spike"));
            Assert.IsFalse(rig.Hub.FatalFired, "minor is not fatal");
        }

        [Test]
        public void AReportCanBePlacedWhereTheProblemIs()
        {
            var rig = new Rig();
            rig.State.Position = new[] { 31f, 1f, 31f };   // the player, 6 m from the wall
            var wall = new[] { 37.5f, 2.5f, 35f };
            rig.Hub.Report("tunneling", "major", null, wall);
            Assert.IsNotNull(rig.Hub.Report("tunneling", "major", null, rig.State.Position), "the player's cell is a different cell");
            Assert.IsNull(rig.Hub.Report("tunneling", "major", null, new[] { 37.9f, 0f, 35.5f }), "same cell as the wall: limited");
            var lines = rig.Events();
            CollectionAssert.AreEqual(new[] { 37.5, 2.5, 35.0 }, lines[0]["pos"].ToObject<double[]>());
            Assert.Throws<ArgumentException>(() => rig.Hub.Report("tunneling", "major", null, new[] { 1f, 2f }));
        }

        [Test]
        public void NoScreenshotServiceMeansNoScreenshotField()
        {
            var rig = new Rig(screenshots: false);
            rig.Hub.Report("stuck", "major", null);
            var data = rig.Events().Single()["data"];
            Assert.IsNull(data["screenshot"]);
            Assert.IsNull(data["details"], "details are optional");
        }

        [Test]
        public void ReportAfterTheRunEndedIsDropped()
        {
            var rig = new Rig();
            rig.Writer.Close("run_end");
            Assert.IsNull(rig.Hub.Report("stuck", "major"));
            Assert.AreEqual(0, rig.Hub.Count("stuck"));
        }

        [TestCase("Stuck", "major")]
        [TestCase("visual:black_screen", "major")]
        [TestCase("stuck", "S2")]
        [TestCase("stuck", null)]
        public void BadNamesAndSeveritiesAreRejected(string detector, string severity)
        {
            Assert.Throws<ArgumentException>(() => new Rig().Hub.Report(detector, severity));
        }

        [Test]
        public void NotImplementedDetectorIsSkippedAndOthersKeepRunning()
        {
            var rig = new Rig();
            var stub = new Throwing("stuck", new NotImplementedException("YOU WRITE"));
            var perf = new PerfSpikeDetector();
            rig.Hub.Add(stub);
            rig.Hub.Add(perf);

            rig.Hub.Tick(rig.Frame(frameMs: 80f));
            rig.Hub.Tick(rig.Frame());

            Assert.AreEqual(1, stub.Ticks, "turned off after the first throw");
            Assert.AreEqual("not implemented yet", rig.Hub.Skipped["stuck"]);
            Assert.AreEqual(0, rig.Hub.Failed.Count, "a YOU WRITE stub is not an internal error");
            Assert.AreEqual(1, rig.Hub.Count("perf_spike"), "the next detector still ran in the same frame");
            Assert.AreEqual(1, rig.Warnings.Count);
        }

        [Test]
        public void ADetectorThatThrowsIsTurnedOffAsAnInternalError()
        {
            var rig = new Rig();
            var broken = new Throwing("my_detector", new InvalidOperationException("boom"));
            rig.Hub.Add(broken);
            rig.Hub.Tick(rig.Frame());
            rig.Hub.Tick(rig.Frame());
            Assert.AreEqual(1, broken.Ticks);
            Assert.AreEqual("InvalidOperationException: boom", rig.Hub.Failed["my_detector"]);
            Assert.IsTrue(rig.Hub.IsOff("my_detector"));
            StringAssert.Contains("boom", rig.Warnings.Single());
        }

        [Test]
        public void DetectorNamesMustBeValidAndUnique()
        {
            var hub = new Rig().Hub;
            hub.Add(new PerfSpikeDetector());
            Assert.Throws<ArgumentException>(() => hub.Add(new PerfSpikeDetector()));
            Assert.Throws<ArgumentException>(() => hub.Add(new Throwing("Bad Name", new Exception())));
        }

        // ---- perf spike -------------------------------------------------------------------------------

        [Test]
        public void PerfSpikeFiresAboveFiftyMillisecondsButNotOnCaptureFrames()
        {
            var detector = new PerfSpikeDetector();
            var recorder = new Recorder();
            detector.Tick(new DetectorFrame(1, 1, 50f, false, null, false, 0), recorder);
            Assert.AreEqual(0, recorder.Reports.Count, "50 ms is the limit, not a spike");
            detector.Tick(new DetectorFrame(1, 2, 143.2f, true, null, false, 0), recorder);
            Assert.AreEqual(0, recorder.Reports.Count, "our own screenshot made this frame slow");
            detector.Tick(new DetectorFrame(1, 3, 143.24f, false, null, false, 0), recorder);
            var report = recorder.Reports.Single();
            Assert.AreEqual(("perf_spike", "minor"), (report.Detector, report.Severity));
            Assert.AreEqual(143.2, (double)report.Details["frame_ms"]);
            Assert.AreEqual(50.0, (double)report.Details["threshold_ms"]);
        }

        [Test]
        public void PerfSpikeIgnoresTheFramesAfterASceneLoad()
        {
            var detector = new PerfSpikeDetector();
            var recorder = new Recorder();
            detector.Suppress(2);
            detector.Tick(new DetectorFrame(1, 1, 400f, false, null, false, 0), recorder);
            detector.Tick(new DetectorFrame(1, 2, 400f, false, null, false, 0), recorder);
            Assert.AreEqual(0, recorder.Reports.Count);
            detector.Tick(new DetectorFrame(1, 3, 400f, false, null, false, 0), recorder);
            Assert.AreEqual(1, recorder.Reports.Count);
        }

        // ---- exception burst --------------------------------------------------------------------------

        [Test]
        public void BurstCounterSlidesAOneSecondWindow()
        {
            var counter = new ExceptionBurstCounter();
            Assert.AreEqual(10, counter.Add(0.0, 10));
            Assert.AreEqual(20, counter.Add(0.5, 10));
            Assert.IsFalse(counter.IsBurst, "20 is the limit: more than 20 is a burst");
            Assert.AreEqual(21, counter.Add(0.99, 1));
            Assert.IsTrue(counter.IsBurst);
            Assert.AreEqual(11, counter.Add(1.0, 0), "the window is (t - 1, t]: the events at 0.0 left");
            Assert.AreEqual(1, counter.Add(1.6, 0));
            counter.Reset();
            Assert.AreEqual(0, counter.Total);
            Assert.Throws<ArgumentOutOfRangeException>(() => counter.Add(2.0, -1));
        }

        [Test]
        public void ExceptionBurstReportsOnceThenNeedsANewBurst()
        {
            var detector = new ExceptionBurstDetector();
            var recorder = new Recorder();
            for (var i = 0; i < 30; i++)
            {
                detector.Tick(new DetectorFrame(i * 0.02, i, 16f, false, null, false, 1), recorder);
            }
            var report = recorder.Reports.Single();
            Assert.AreEqual(("exception_burst", "major"), (report.Detector, report.Severity));
            Assert.AreEqual(21, (int)report.Details["exceptions"]);
            Assert.AreEqual(1.0, (double)report.Details["window_s"]);
        }

        // ---- fall -------------------------------------------------------------------------------------

        [Test]
        public void FallReportsOncePerFallAndRespawns()
        {
            var respawns = 0;
            var detector = new FallDetector(() => -5.5f, () => respawns++);
            var recorder = new Recorder();
            detector.Tick(new DetectorFrame(1.0, 1, 16f, false, new[] { 34.9f, 0f, 1.3f }, true, 0), recorder);
            detector.Tick(new DetectorFrame(1.1, 2, 16f, false, new[] { 35.0f, 0f, 1.2f }, true, 0), recorder);   // standing
            detector.Tick(new DetectorFrame(1.5, 3, 16f, false, new[] { 35.1f, -2f, 1.1f }, true, 0), recorder);  // falling
            Assert.AreEqual(0, recorder.Reports.Count);
            detector.Tick(new DetectorFrame(1.8, 4, 16f, false, new[] { 35.2f, -6f, 1.1f }, true, 0), recorder);
            detector.Tick(new DetectorFrame(1.9, 5, 16f, false, new[] { 35.2f, -8f, 1.1f }, true, 0), recorder);  // respawn not applied yet

            var report = recorder.Reports.Single();
            Assert.AreEqual(("fell_out_of_world", "critical"), (report.Detector, report.Severity));
            Assert.AreEqual(-5.5, (double)report.Details["kill_plane_y"]);
            CollectionAssert.AreEqual(new[] { 35.0, 0.0, 1.2 }, report.Details["last_grounded_pos"].ToObject<double[]>().Select(v => Math.Round(v, 2)));
            Assert.AreEqual(1, respawns);

            detector.Tick(new DetectorFrame(2.0, 6, 16f, false, new[] { 2f, 1.1f, 2f }, true, 0), recorder);       // back at the checkpoint
            detector.Tick(new DetectorFrame(5.0, 7, 16f, false, new[] { 2f, -9f, 2f }, true, 0), recorder);
            Assert.AreEqual(2, recorder.Reports.Count, "a second fall is a second report");
            Assert.AreEqual(2, respawns);
        }

        [Test]
        public void FallWithoutAPlayerOrMoverDoesNotThrow()
        {
            var detector = new FallDetector(() => 0f, null);
            var recorder = new Recorder();
            detector.Tick(new DetectorFrame(1, 1, 16f, false, null, false, 0), recorder);
            detector.Tick(new DetectorFrame(2, 2, 16f, false, new[] { 0f, -1f, 0f }, false, 0), recorder);
            Assert.AreEqual(1, recorder.Reports.Count);
            Assert.IsNull(recorder.Reports[0].Details["last_grounded_pos"], "never seen standing");
        }

        // ---- exit codes -------------------------------------------------------------------------------

        [TestCase(false, false, 0)]
        [TestCase(true, false, 1)]
        [TestCase(false, true, 2)]
        [TestCase(true, true, 2)]
        public void ExitCodeFollowsTheSpec(bool fatal, bool internalError, int expected)
        {
            Assert.AreEqual(expected, ExitCodes.For(fatal, internalError));
        }
    }
}
