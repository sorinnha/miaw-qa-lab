using System.Collections.Generic;
using System.IO;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    /// <summary>Golden tests: the C# serializer agrees with schemas/examples/events_valid.jsonl (spec 00).</summary>
    public class QAEventJsonTests
    {
        private static IEnumerable<string> ValidLines()
        {
            foreach (var line in RepoPaths.ReadText("schemas", "examples", "events_valid.jsonl").Split('\n'))
            {
                if (line.Trim().Length > 0)
                {
                    yield return line.Trim();
                }
            }
        }

        private static JObject Example(string kind)
        {
            foreach (var line in ValidLines())
            {
                var obj = (JObject)Json.Parse(line);
                if ((string)obj["kind"] == kind)
                {
                    return obj;
                }
            }
            throw new KeyNotFoundException(kind);
        }

        [Test]
        public void ComparisonTreatsNumbersByValueButCatchesRealDifferences()
        {
            Assert.IsTrue(Json.Equivalent(Json.Parse("{\"a\":55}"), Json.Parse("{\"a\":55.0}")));
            Assert.IsFalse(Json.Equivalent(Json.Parse("{\"a\":1}"), Json.Parse("{\"a\":2}")));
            Assert.IsFalse(Json.Equivalent(Json.Parse("{\"a\":1}"), Json.Parse("{\"a\":1,\"b\":null}")));
            Assert.IsFalse(Json.Equivalent(Json.Parse("[1,2]"), Json.Parse("[2,1]")));
            Assert.IsFalse(Json.Equivalent(Json.Parse("{\"a\":\"1\"}"), Json.Parse("{\"a\":1}")));
        }

        [Test]
        public void EveryValidExampleRoundTrips()
        {
            var count = 0;
            foreach (var line in ValidLines())
            {
                Json.AssertSame(line, QAEvent.FromJson(line).ToJsonLine(), line);
                count++;
            }
            Assert.AreEqual(9, count);
        }

        [Test]
        public void SerializedLineIsCompactAndHasNoNewline()
        {
            var line = QAEvent.FromJson(Example("log").ToString()).ToJsonLine();
            StringAssert.DoesNotContain("\n", line);
            StringAssert.StartsWith("{\"schema\":\"qalab.event/1\",\"run_id\":", line);
        }

        /// <summary>Build an event through EventWriter from the example's values.</summary>
        private static string Write(JObject example, System.Func<EventWriter, QAEvent> make)
        {
            var clock = new FakeClock
            {
                Seconds = (double)example["t"],
                UtcNow = System.DateTime.Parse((string)example["ts"], null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal),
            };
            var pos = example["pos"].Type == JTokenType.Null ? null : example["pos"].ToObject<float[]>();
            var state = new FakeState { Scene = (string)example["scene"], Frame = (long)example["frame"], Position = pos };
            var output = new StringWriter();
            using (var writer = new EventWriter((string)example["run_id"], clock, state, output))
            {
                // Advance seq to the example's value.
                var seq = (long)example["seq"];
                for (var i = 0; i < seq; i++)
                {
                    writer.Enqueue(EventKinds.Marker, new JObject { ["marker"] = "run_start" });
                }
                writer.Drain();
                output.GetStringBuilder().Clear();
                make(writer);
            }
            return output.ToString().TrimEnd('\n');
        }

        [Test]
        public void LogEventFromWriterMatchesExample()
        {
            var ex = Example("log");
            var actual = Write(ex, w => w.Log((string)ex["level"], (string)ex["message"], (string)ex["stack"]));
            Json.AssertSame(ex.ToString(), actual);
        }

        [Test]
        public void InfoLogWithoutStackWritesStackNull()
        {
            JObject info = null;
            foreach (var line in ValidLines())
            {
                var obj = (JObject)Json.Parse(line);
                if ((string)obj["level"] == "info") info = obj;
            }
            Assert.IsNotNull(info);
            var actual = Write(info, w => w.Log("info", (string)info["message"], ""));
            Json.AssertSame(info.ToString(), actual);
        }

        [Test]
        public void MarkerEventFromWriterMatchesExample()
        {
            var ex = Example("marker");
            var actual = Write(ex, w => w.Marker((string)ex["data"]["marker"], (JObject)ex["data"]["details"]));
            Json.AssertSame(ex.ToString(), actual);
        }

        [Test]
        public void MetricEventFromWriterMatchesExample()
        {
            var ex = Example("metric");
            var d = ex["data"];
            var actual = Write(ex, w => w.Metric((double)d["fps"], (double)d["frame_ms"], (double)d["frame_ms_p95"], (double)d["gc_mb"], (double)d["mem_mb"]));
            Json.AssertSame(ex.ToString(), actual);
        }

        [Test]
        public void MetricWithoutMemoryOmitsMemMb()
        {
            var ex = Example("metric");
            var actual = (JObject)Json.Parse(Write(ex, w => w.Metric(60, 16.6, 18.2, 38.2, null)));
            Assert.IsNull(actual["data"]["mem_mb"]);
            Assert.AreEqual(38.2, (double)actual["data"]["gc_mb"]);
        }

        [Test]
        public void NonLogEventsHaveNoStackKey()
        {
            var ex = Example("action");
            var actual = (JObject)Json.Parse(Write(ex, w => w.Enqueue(EventKinds.Action, (JObject)ex["data"])));
            Assert.IsFalse(actual.ContainsKey("stack"));
            Assert.IsFalse(actual.ContainsKey("level"));
            Json.AssertSame(ex.ToString(), actual.ToString());
        }
    }
}
