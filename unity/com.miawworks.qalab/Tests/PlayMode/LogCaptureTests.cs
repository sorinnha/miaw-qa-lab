using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiawWorks.QALab.Tests
{
    /// <summary>Spec 01 PlayMode: LogCapture records Debug.LogError and an exception thrown in a coroutine.</summary>
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
                Debug.Log("info is below the minimum level");
                Debug.LogWarning("[QALab] own message, ignored");   // warnings never fail a Unity test
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

            var lines = Lines(output);
            Assert.AreEqual(2, lines.Count, output.ToString());
            Assert.AreEqual("error", (string)lines[0]["level"]);
            Assert.AreEqual("qalab test error", (string)lines[0]["message"]);
            Assert.AreEqual("exception", (string)lines[1]["level"]);
            StringAssert.Contains("InvalidOperationException: qalab coroutine boom", (string)lines[1]["message"]);
            StringAssert.Contains("Thrower", (string)lines[1]["stack"]);
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
