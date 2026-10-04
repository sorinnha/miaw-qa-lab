using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiawWorks.QALab.Tests
{
    /// <summary>
    /// Spec 01 PlayMode: LogCapture records Debug.LogError, an exception thrown in a coroutine and a log
    /// from a worker thread (logMessageReceivedThreaded), and skips info logs and its own [QALab] logs.
    /// </summary>
    public class LogCaptureTests
    {
        private sealed class Clock : IClock
        {
            public double Seconds => 1.0;
            public DateTime UtcNow => new DateTime(2026, 10, 5, 10, 30, 0, DateTimeKind.Utc);
        }

        private sealed class State : IMainThreadState
        {
            public string Scene => "Test";
            public long Frame => 1;
            public float[] Position => null;
        }

        public sealed class Thrower : MonoBehaviour
        {
            public IEnumerator Throw()
            {
                yield return null;
                throw new InvalidOperationException("qalab coroutine boom");
            }
        }

        private static List<JObject> Lines(StringWriter output)
        {
            var lines = new List<JObject>();
            foreach (var line in output.ToString().Split('\n'))
            {
                if (line.Length > 0) lines.Add(JObject.Parse(line));   // every line parses
            }
            return lines;
        }

        [UnityTest]
        public IEnumerator CapturesErrorsAndCoroutineExceptionsButNotOwnLogs()
        {
            var output = new StringWriter();
            var writer = new EventWriter("test-run", new Clock(), new State(), output);
            var capture = new LogCapture(writer, LogLevels.Warning);
            var go = new GameObject("thrower");
            try
            {
                capture.Start();
                LogAssert.Expect(LogType.Error, "qalab test error");
                Debug.LogError("qalab test error");
                Debug.Log("qalab info is below the minimum level");
                Debug.LogWarning("[QALab] own message, ignored");   // warnings never fail a Unity test
                // The threaded callback runs on the logging thread, before Wait returns.
                Task.Run(() => Debug.LogWarning("qalab worker thread warning")).Wait();
                LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: qalab coroutine boom"));
                var thrower = go.AddComponent<Thrower>();
                thrower.StartCoroutine(thrower.Throw());
                yield return null;
                yield return null;
            }
            finally
            {
                capture.Stop();
                UnityEngine.Object.Destroy(go);
            }
            writer.Drain();

            // Only this test's lines: Unity can log unrelated warnings meanwhile (e.g. no audio listener).
            // Every message above contains "qalab", so a skipped one that leaks in still fails the count.
            var lines = Lines(output).FindAll(
                l => ((string)l["message"]).IndexOf("qalab", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.AreEqual(3, lines.Count, output.ToString());
            Assert.AreEqual("error", (string)lines[0]["level"]);
            Assert.AreEqual("qalab test error", (string)lines[0]["message"]);
            Assert.AreEqual("warning", (string)lines[1]["level"]);
            Assert.AreEqual("qalab worker thread warning", (string)lines[1]["message"]);
            Assert.AreEqual("exception", (string)lines[2]["level"]);
            StringAssert.Contains("InvalidOperationException: qalab coroutine boom", (string)lines[2]["message"]);
            StringAssert.Contains("Thrower", (string)lines[2]["stack"]);
        }

        [Test]
        public void LevelMappingFollowsSpec00()
        {
            Assert.AreEqual("info", LogCapture.LevelOf(LogType.Log));
            Assert.AreEqual("warning", LogCapture.LevelOf(LogType.Warning));
            Assert.AreEqual("error", LogCapture.LevelOf(LogType.Error));
            Assert.AreEqual("exception", LogCapture.LevelOf(LogType.Exception));
            Assert.AreEqual("assert", LogCapture.LevelOf(LogType.Assert));
        }
    }
}
