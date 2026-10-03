using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiawWorks.QALab.Tests
{
    /// <summary>
    /// The real run wiring end to end: QALabHost + RunContext + LogCapture + MetricsSampler +
    /// LabelRecorder write a run folder whose files follow spec 00.
    /// </summary>
    public class RunRecordingTests
    {
        private string _outDir;
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _outDir = Path.Combine(Path.GetTempPath(), "qalab-playmode-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_outDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) UnityEngine.Object.Destroy(_go);
            LabelRecorder.UseCatalog(new SeedCatalogEntry[0]);
            try { Directory.Delete(_outDir, true); } catch (IOException) { /* best effort */ }
        }

        [UnityTest]
        public IEnumerator RecordsAValidRunFolder()
        {
            LabelRecorder.UseCatalog(new[]
            {
                new SeedCatalogEntry
                {
                    BugId = "SB99", Type = "log", Title = "test seed", Feature = "Doors", ExpectedSeverity = "S3",
                    Match = new MatchRule { MessageRegex = "^qalab wiring error" },
                },
            });
            var options = new QALabOptions { Enabled = true, DurationS = 30f, Adapter = "manual", Benchmark = true, Seed = 3 };
            var runId = RunIds.Create(DateTime.UtcNow, options.Seed);
            var runDir = Path.Combine(_outDir, runId);
            Directory.CreateDirectory(runDir);

            _go = new GameObject("QALab-test");
            var host = _go.AddComponent<QALabHost>();
            host.Begin(options, runId, runDir);
            Assert.IsTrue(host.IsRecording);
            var started = JObject.Parse(File.ReadAllText(Path.Combine(runDir, "run.json")));
            Assert.AreEqual(JTokenType.Null, started["ended_at"].Type, "no ended_at until a clean end");

            LogAssert.Expect(LogType.Error, "qalab wiring error 7");
            Debug.LogError("qalab wiring error 7");
            LabelRecorder.Trigger("SB99");
            yield return new WaitForSecondsRealtime(1.3f);   // ≥ 1 metric, ≥ 1 periodic drain
            host.EndRun(ExitReasons.TestFinished);
            Assert.IsFalse(host.IsRecording);

            var run = JObject.Parse(File.ReadAllText(Path.Combine(runDir, "run.json")));
            Assert.AreEqual("qalab.run/1", (string)run["schema"]);
            Assert.AreEqual(runId, (string)run["run_id"]);
            Assert.AreEqual("editor_playmode", (string)run["mode"]);
            Assert.AreEqual("test_finished", (string)run["exit_reason"]);
            Assert.AreEqual(JTokenType.String, run["ended_at"].Type);

            var kinds = new List<string>();
            var seqs = new List<long>();
            JObject log = null;
            foreach (var line in File.ReadAllLines(Path.Combine(runDir, "events.jsonl")))
            {
                var e = JObject.Parse(line);   // every line parses
                Assert.AreEqual("qalab.event/1", (string)e["schema"]);
                kinds.Add((string)e["kind"]);
                seqs.Add((long)e["seq"]);
                if ((string)e["kind"] == "log") log = e;
                if ((string)e["kind"] == "marker" && (string)e["data"]["marker"] == "run_start")
                {
                    Assert.AreEqual(0L, (long)e["seq"], "run_start is the first event");
                }
            }
            seqs.Sort();
            for (var i = 0; i < seqs.Count; i++) Assert.AreEqual(i, seqs[i], "seq has no gaps");
            CollectionAssert.Contains(kinds, "metric");
            Assert.AreEqual("marker", kinds[kinds.Count - 1], "run_end is written last");
            Assert.IsNotNull(log, "the error was captured");
            Assert.AreEqual("error", (string)log["level"]);
            Assert.AreEqual("qalab wiring error 7", (string)log["message"]);

            var labels = JObject.Parse(File.ReadAllText(Path.Combine(runDir, "labels.json")));
            Assert.AreEqual("qalab.labels/1", (string)labels["schema"]);
            Assert.AreEqual("SB99", (string)labels["seeded_bugs"][0]["bug_id"]);
            Assert.AreEqual(1, ((JArray)labels["seeded_bugs"][0]["triggers"]).Count);
        }
    }
}
