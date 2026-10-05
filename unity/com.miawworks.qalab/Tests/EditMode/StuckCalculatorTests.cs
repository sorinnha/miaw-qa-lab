// Engine-free: runs in Unity's Test Runner and in tools/cs-check.
// These tests describe StuckCalculator and StuckDetector (an M4 learning task, written by Claude at
// Sora's request, D-030).
using System.Collections.Generic;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    public class StuckCalculatorTests
    {
        private sealed class Recorder : IDetectorReporter
        {
            public readonly List<(string Detector, string Severity, JObject Details)> Reports = new List<(string, string, JObject)>();

            public QAEvent Report(string detector, string severity, JObject details = null, float[] pos = null)
            {
                Reports.Add((detector, severity, details));
                return new QAEvent();
            }
        }

        // Times are multiples of 1/4 or 1/64 s: exact in binary, so "exactly 4 s ago" is exact too.

        /// <summary>Feed samples every <paramref name="dt"/> s from <paramref name="from"/> to <paramref name="to"/>; returns the last answer.</summary>
        private static bool Feed(StuckCalculator calc, double from, double to, double dt, float x, float z, bool moving = true)
        {
            var stuck = false;
            for (var t = from; t <= to; t += dt)
            {
                stuck = calc.Add(t, x, z, moving);
            }
            return stuck;
        }

        private static DetectorFrame Frame(double t, float[] pos, bool moving) => new DetectorFrame(t, 0, 16f, false, pos, moving, 0);

        [Test]
        public void NotStuckBeforeAWholeWindowOfMoving()
        {
            var calc = new StuckCalculator();
            Assert.IsFalse(Feed(calc, 0.0, 3.75, 0.25, 5f, 5f), "standing still for 3.75 s is not yet 4 s");
            Assert.IsTrue(calc.Add(4.0, 5f, 5f, true), "4 s without moving while the bot wants to move");
        }

        [Test]
        public void WalkingNormallyIsNeverStuck()
        {
            var calc = new StuckCalculator();
            var stuck = false;
            for (var i = 0; i <= 640; i++)   // 10 s at 64 fps, 4.5 m/s along x
            {
                var t = i / 64.0;
                stuck |= calc.Add(t, (float)(4.5 * t), 0f, true);
            }
            Assert.IsFalse(stuck);
        }

        [Test]
        public void DistanceIsMeasuredOnTheGroundFromTheAnchor()
        {
            var calc = new StuckCalculator();
            calc.Add(0.0, 0f, 0f, true);
            calc.Add(2.0, 0.3f, 0f, true);
            Assert.IsTrue(calc.Add(4.0, 0.3f, 0.3f, true), "√(0.09 + 0.09) ≈ 0.42 m < 0.5 m from the anchor at t = 0");
            var calc2 = new StuckCalculator();
            calc2.Add(0.0, 0f, 0f, true);
            Assert.IsFalse(calc2.Add(4.0, 0.3f, 0.45f, true), "√(0.09 + 0.2025) ≈ 0.54 m: it moved");
            var calc3 = new StuckCalculator();
            calc3.Add(0.0, 0f, 0f, true);
            Assert.IsFalse(calc3.Add(4.0, 0.5f, 0f, true), "exactly 0.5 m is not less than 0.5 m");
        }

        [Test]
        public void TheAnchorIsTheNewestSampleAtLeastOneWindowOld()
        {
            var calc = new StuckCalculator();
            calc.Add(0.0, 0f, 0f, true);     // far from where the player ends up
            calc.Add(1.0, 10f, 0f, true);    // the newest sample with t <= 5 - 4: the anchor at t = 5
            calc.Add(3.0, 10f, 0f, true);
            Assert.IsTrue(calc.Add(5.0, 10.2f, 0f, true), "anchor (10, 0) is 0.2 m away; the t = 0 sample is not the anchor");
        }

        [Test]
        public void JitteringInPlaceIsStuck()
        {
            var calc = new StuckCalculator();
            var stuck = false;
            for (var i = 0; i <= 320; i++)   // pushing into a wall: ±0.2 m back and forth for 5 s
            {
                stuck = calc.Add(i / 64.0, i % 2 == 0 ? 0.2f : -0.2f, 0f, true);
            }
            Assert.IsTrue(stuck);
        }

        [Test]
        public void StoppingOnPurposeClearsTheHistory()
        {
            var calc = new StuckCalculator();
            Feed(calc, 0.0, 3.5, 0.5, 1f, 1f);
            Assert.IsFalse(calc.Add(3.75, 1f, 1f, false), "the bot stopped asking to move");
            Assert.IsFalse(Feed(calc, 4.0, 7.75, 0.25, 1f, 1f), "a new window starts at 4.0");
            Assert.IsTrue(calc.Add(8.0, 1f, 1f, true));
        }

        [Test]
        public void ResetStartsANewWindow()
        {
            var calc = new StuckCalculator();
            Assert.IsTrue(Feed(calc, 0.0, 4.0, 0.5, 0f, 0f));
            calc.Reset();
            Assert.AreEqual(0, calc.SampleCount);
            Assert.IsFalse(calc.Add(4.5, 0f, 0f, true));
        }

        [Test]
        public void MemoryStaysBounded()
        {
            var calc = new StuckCalculator();
            for (var i = 0; i < 64 * 60; i++)   // one minute at 64 fps
            {
                calc.Add(i / 64.0, i * 0.1f, 0f, true);
            }
            Assert.LessOrEqual(calc.SampleCount, 4 * 64 + 2, "about one window of samples, not the whole minute");
            Assert.Greater(calc.SampleCount, 0);
        }

        [Test]
        public void TimeGoingBackwardsStartsANewWindow()
        {
            var calc = new StuckCalculator();
            calc.Add(0.0, 0f, 0f, true);
            calc.Add(4.0, 0f, 0f, true);
            Assert.IsFalse(calc.Add(1.0, 0f, 0f, true), "a clock reset is not 4 s of standing still");
            Assert.AreEqual(1, calc.SampleCount);
        }

        [Test]
        public void ANaNTimeIsIgnoredAndMemoryStaysBounded()
        {
            var calc = new StuckCalculator();
            Assert.IsFalse(calc.Add(double.NaN, 0f, 0f, true));
            for (int i = 0; i <= 60 * 20; i++) calc.Add(i / 60.0, 0f, 0f, true);
            Assert.LessOrEqual(calc.SampleCount, 4 * 64 + 2, "a NaN sample must not block the queue");
        }

        [Test]
        public void CustomWindowAndDistance()
        {
            var calc = new StuckCalculator(windowS: 2.0, minDistanceM: 1.0f);
            calc.Add(0.0, 0f, 0f, true);
            Assert.IsTrue(calc.Add(2.0, 0.9f, 0f, true));
        }

        [Test]
        public void DetectorReportsMajorOncePerWindow()
        {
            var detector = new StuckDetector();
            var recorder = new Recorder();
            for (var i = 0; i <= 48; i++)   // 12 s at 4 Hz, standing still while asked to move
            {
                detector.Tick(Frame(i * 0.25, new[] { 3f, 0f, 3f }, true), recorder);
            }
            // Reported at t = 4.0 (anchor 0.0), then the window restarts at 4.25: reported at 8.25.
            // The next would be 12.5, after the last tick.
            Assert.AreEqual(2, recorder.Reports.Count);
            var report = recorder.Reports[0];
            Assert.AreEqual(("stuck", "major"), (report.Detector, report.Severity));
            Assert.AreEqual(4.0, (double)report.Details["window_s"]);
            Assert.AreEqual(0.5, (double)report.Details["min_distance_m"]);
        }

        [Test]
        public void DetectorIgnoresFramesWithoutAPlayerOrWithoutAMoveTarget()
        {
            var detector = new StuckDetector();
            var recorder = new Recorder();
            for (var i = 0; i <= 40; i++)
            {
                detector.Tick(Frame(i * 0.25, null, true), recorder);                   // no player registered
            }
            for (var i = 41; i <= 80; i++)
            {
                detector.Tick(Frame(i * 0.25, new[] { 3f, 0f, 3f }, false), recorder);  // the bot isn't moving
            }
            Assert.AreEqual(0, recorder.Reports.Count);
        }
    }
}
