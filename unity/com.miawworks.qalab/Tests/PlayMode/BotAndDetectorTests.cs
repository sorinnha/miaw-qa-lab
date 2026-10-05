using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace MiawWorks.QALab.Tests
{
    /// <summary>
    /// Spec 01 PlayMode tests for M4: the bot runner drives a fake player, the fall detector reports and
    /// respawns, screenshots land in shots/ with events, and the run's results.xml and exit code follow.
    /// Each test records a real run (QALabHost) in a temp folder.
    /// </summary>
    public class BotAndDetectorTests
    {
        /// <summary>A player that walks straight at its target, 5 m/s, and respawns at the origin.</summary>
        public sealed class FakeMover : MonoBehaviour, IBotMover
        {
            public Vector3? Target;
            public int Respawns;

            public void MoveTowards(Vector3 worldTarget) => Target = worldTarget;
            public void Stop() => Target = null;
            public bool TryInteract(out string objectName)
            {
                objectName = null;
                return false;
            }

            public void Respawn()
            {
                Respawns++;
                Target = null;
                transform.position = Vector3.zero;
            }

            private void Update()
            {
                if (Target.HasValue) transform.position = Vector3.MoveTowards(transform.position, Target.Value, 5f * Time.deltaTime);
            }
        }

        /// <summary>Walks to a random point within 3 m every step and logs it.</summary>
        private sealed class TestWalker : IBotAdapter
        {
            public string Name => "test_walker";
            public void Begin(BotContext ctx) { }
            public BotStepResult Step(BotContext ctx)
            {
                var target = ctx.Player.position + new Vector3(ctx.Random.Range(-3f, 3f), 0f, ctx.Random.Range(-3f, 3f));
                ctx.Mover.MoveTowards(target);
                ctx.LogAction("move_to", target);
                return BotStepResult.Continue;
            }
            public void End(BotContext ctx) { }
        }

        private readonly List<GameObject> _objects = new List<GameObject>();
        private string _outDir;
        private string _savedLastRunDir;

        [SetUp]
        public void SetUp()
        {
            _outDir = Path.Combine(Path.GetTempPath(), "qalab-m4-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_outDir);
#if UNITY_EDITOR
            _savedLastRunDir = UnityEditor.EditorPrefs.HasKey("QALab.LastRunDir") ? UnityEditor.EditorPrefs.GetString("QALab.LastRunDir") : null;
#endif
            BotAdapterRegistry.Register("test_walker", () => new TestWalker());
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) UnityEngine.Object.Destroy(go);
            _objects.Clear();
            QALab.RegisterPlayer(null, null);
            QALab.KillPlaneY = null;
            try { Directory.Delete(_outDir, true); } catch (IOException) { /* best effort */ }
#if UNITY_EDITOR
            if (_savedLastRunDir == null) UnityEditor.EditorPrefs.DeleteKey("QALab.LastRunDir");
            else UnityEditor.EditorPrefs.SetString("QALab.LastRunDir", _savedLastRunDir);
#endif
        }

        private FakeMover AddPlayer()
        {
            var go = new GameObject("TestPlayer");
            _objects.Add(go);
            var mover = go.AddComponent<FakeMover>();
            QALab.RegisterPlayer(go.transform, mover);
            return mover;
        }

        private QALabHost StartRun(QALabOptions options, out string runDir)
        {
            var runId = RunIds.Create(DateTime.UtcNow, options.Seed) + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
            runDir = Path.Combine(_outDir, runId);
            Directory.CreateDirectory(runDir);
            var go = new GameObject("QALab-test");
            _objects.Add(go);
            var host = go.AddComponent<QALabHost>();
            host.Begin(options, runId, runDir);
            return host;
        }

        private static List<JObject> Events(string runDir) =>
            File.ReadAllLines(Path.Combine(runDir, "events.jsonl")).Select(JObject.Parse).ToList();

        [UnityTest]
        public IEnumerator BotRunnerDrivesThePlayerForFiveSeconds()
        {
            var player = AddPlayer();
            var host = StartRun(new QALabOptions { Enabled = true, Adapter = "test_walker", Seed = 4, DurationS = 60f, ShotEveryS = 0f }, out var runDir);
            yield return new WaitForSecondsRealtime(5f);
            var moved = Vector3.Distance(player.transform.position, Vector3.zero);
            host.EndRun(ExitReasons.TestFinished);

            var events = Events(runDir);
            var actions = events.Where(e => (string)e["kind"] == "action").ToList();
            Assert.GreaterOrEqual(actions.Count, 10, "a step every 0.25 s for 5 s");
            Assert.Greater(moved, 0.5f, "the player moved");
            CollectionAssert.AreEqual(Enumerable.Range(1, actions.Count), actions.Select(a => (int)a["data"]["step"]), "steps count from 1");
            Assert.IsTrue(actions.All(a => (string)a["data"]["adapter"] == "test_walker"));
            var markers = events.Where(e => (string)e["kind"] == "marker").Select(e => (string)e["data"]["marker"]).ToList();
            Assert.Less(markers.IndexOf("bot_started"), markers.IndexOf("bot_stopped"));
            Assert.AreEqual("run_end", markers.Last());
            Assert.AreEqual(0, (int)JObject.Parse(File.ReadAllText(Path.Combine(runDir, "run.json")))["exit_code"]);
        }

        [UnityTest]
        public IEnumerator FallingBelowTheKillPlaneIsReportedAndRespawns()
        {
            var player = AddPlayer();
            QALab.KillPlaneY = -5f;
            var host = StartRun(new QALabOptions { Enabled = true, Adapter = "manual", DurationS = 60f, ShotEveryS = 0f }, out var runDir);
            yield return null;
            yield return null;
            player.transform.position = new Vector3(3f, -20f, 4f);
            yield return null;
            yield return null;
            host.EndRun(ExitReasons.TestFinished);

            Assert.AreEqual(1, player.Respawns, "the detector asked the game to respawn");
            Assert.AreEqual(Vector3.zero, player.transform.position);
            // By name: a slow editor frame may add a perf_spike event to the same run.
            var fall = Events(runDir).Single(e => (string)e["kind"] == "detector" && (string)e["data"]["detector"] == "fell_out_of_world");
            Assert.AreEqual("fell_out_of_world", (string)fall["data"]["detector"]);
            Assert.AreEqual("critical", (string)fall["data"]["severity"]);
            Assert.AreEqual(-5.0, (double)fall["data"]["details"]["kill_plane_y"]);

            var run = JObject.Parse(File.ReadAllText(Path.Combine(runDir, "run.json")));
            Assert.AreEqual(1, (int)run["exit_code"], "a critical detector fails the run");
            var results = XDocument.Load(Path.Combine(runDir, "results.xml"));
            var testCase = results.Descendants("testcase").Single(c => (string)c.Attribute("name") == "fell_out_of_world");
            StringAssert.StartsWith("1 fell_out_of_world event(s)", (string)testCase.Element("failure").Attribute("message"));
        }

        [UnityTest]
        public IEnumerator UnknownAdapterIsAnInternalError()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("unknown bot adapter 'no_such_bot'"));
            var host = StartRun(new QALabOptions { Enabled = true, Adapter = "no_such_bot", DurationS = 60f, ShotEveryS = 0f }, out var runDir);
            yield return null;
            host.EndRun(ExitReasons.TestFinished);
            Assert.AreEqual(2, (int)JObject.Parse(File.ReadAllText(Path.Combine(runDir, "run.json")))["exit_code"]);
            var results = XDocument.Load(Path.Combine(runDir, "results.xml"));
            Assert.IsNotNull(results.Descendants("testcase").Single(c => (string)c.Attribute("name") == "no_internal_errors").Element("error"));
        }

        [UnityTest]
        public IEnumerator PeriodicScreenshotsAreSavedWithEvents()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Assert.Ignore("no graphics device (-nographics): nothing to capture");
            }
            var cameraGo = new GameObject("TestCamera") { tag = "MainCamera" };
            _objects.Add(cameraGo);
            cameraGo.AddComponent<Camera>();
            var host = StartRun(new QALabOptions { Enabled = true, Adapter = "manual", DurationS = 60f, ShotEveryS = 0.3f }, out var runDir);
            yield return new WaitForSecondsRealtime(1.2f);
            host.EndRun(ExitReasons.TestFinished);

            var shots = Events(runDir).Where(e => (string)e["kind"] == "screenshot").ToList();
            Assert.GreaterOrEqual(shots.Count, 2);
            foreach (var shot in shots)
            {
                var path = (string)shot["data"]["path"];
                StringAssert.IsMatch("^shots/[0-9]{6}\\.png$", path);
                Assert.IsTrue(File.Exists(Path.Combine(runDir, path)), path);
                Assert.AreEqual("periodic", (string)shot["data"]["reason"]);
                Assert.LessOrEqual(Math.Max((int)shot["data"]["w"], (int)shot["data"]["h"]), 1280);
                CollectionAssert.Contains(new[] { "screen_capture", "camera_render" }, (string)shot["data"]["method"]);
            }
        }
    }
}
