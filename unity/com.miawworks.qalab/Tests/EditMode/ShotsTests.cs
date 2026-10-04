// Engine-free: runs in Unity's Test Runner and in tools/cs-check.
using System;
using System.Linq;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    /// <summary>Spec 01 "Screenshots": names, the 1280 px cap, event data and when shots are taken.</summary>
    public class ShotsTests
    {
        [Test]
        public void PathsAreSixDigitsUnderShots()
        {
            Assert.AreEqual("shots/000001.png", Shots.RelativePath(1));
            Assert.AreEqual("shots/123456.png", Shots.RelativePath(123456));
            Assert.Throws<ArgumentOutOfRangeException>(() => Shots.RelativePath(0));
        }

        [TestCase(1280, 720, 1280, 720)]
        [TestCase(1920, 1080, 1280, 720)]
        [TestCase(2560, 1440, 1280, 720)]
        [TestCase(1080, 1920, 720, 1280)]
        [TestCase(3440, 1440, 1280, 536)]
        [TestCase(640, 480, 640, 480)]
        [TestCase(5000, 1, 1280, 1)]
        public void FitCapsTheLongSideAndKeepsTheAspect(int w, int h, int expectedW, int expectedH)
        {
            Assert.AreEqual((expectedW, expectedH), Shots.Fit(w, h));
        }

        [Test]
        public void DataMatchesTheSampleScreenshotEvent()
        {
            var sample = RepoPaths.ReadText("samples", "sample_run", "events.jsonl").Split('\n')
                .Where(l => l.Length > 0).Select(l => (JObject)Json.Parse(l))
                .First(e => (string)e["kind"] == "screenshot" && (string)e["data"]["reason"] == "detector");
            var data = Shots.Data("shots/000003.png", Shots.Detector, 160, 90, Shots.ScreenCapture);
            Json.AssertSame(sample["data"].ToString(), data.ToString());
        }

        [TestCase("shots/1.png", "auto", "screen_capture")]
        [TestCase("shots/1.png", "periodic", "gpu")]
        [TestCase("", "periodic", "screen_capture")]
        public void DataRejectsValuesTheSchemaRejects(string path, string reason, string method)
        {
            Assert.Throws<ArgumentException>(() => Shots.Data(path, reason, 1, 1, method));
        }

        [Test]
        public void PeriodicShotsFollowTheIntervalWithoutCatchingUp()
        {
            var planner = new ShotPlanner(5.0);
            Assert.IsNull(planner.TakeDue(4.9));
            var first = planner.TakeDue(5.0);
            Assert.AreEqual(("shots/000001.png", "periodic"), (first.Path, first.Reason));
            Assert.IsNull(planner.TakeDue(5.1), "one shot per due time");
            var second = planner.TakeDue(17.3);   // a long hitch skipped 10 and 15
            Assert.AreEqual(2, second.Number);
            Assert.IsNull(planner.TakeDue(19.9), "next is 20, not a burst for the missed ones");
            Assert.AreEqual(3, planner.TakeDue(20.0).Number);
        }

        [Test]
        public void DetectorsInOneFrameShareOneShotAndAbsorbAPeriodicOne()
        {
            var planner = new ShotPlanner(5.0);
            var a = planner.RequestDetectorShot();
            var b = planner.RequestDetectorShot();
            Assert.AreEqual("shots/000001.png", a);
            Assert.AreEqual(a, b, "two detectors in the same frame point to the same file");
            var due = planner.TakeDue(5.0);   // the periodic shot was due too
            Assert.AreEqual((1, "detector"), (due.Number, due.Reason));
            Assert.IsNull(planner.TakeDue(5.0));
            Assert.IsNull(planner.TakeDue(9.9), "the periodic schedule moved on to 10");
            Assert.AreEqual(2, planner.TakeDue(10.0).Number);
        }

        [Test]
        public void ManualShotsWorkWithPeriodicOffAndDetectorWinsTheReason()
        {
            var planner = new ShotPlanner(0.0);
            Assert.IsNull(planner.TakeDue(100.0), "0 = no periodic shots");
            planner.RequestManualShot();
            Assert.AreEqual("manual", planner.TakeDue(100.1).Reason);
            planner.RequestManualShot();
            planner.RequestDetectorShot();
            var due = planner.TakeDue(100.2);
            Assert.AreEqual((2, "detector"), (due.Number, due.Reason));
            Assert.AreEqual(2, planner.Planned);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ShotPlanner(-1));
        }
    }
}
