// Engine-free test helpers: compiled by Unity's Test Runner and by tools/cs-check.
using System;
using System.IO;
using MiawWorks.QALab;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    /// <summary>Finds repository files by walking up from where the tests run.</summary>
    public static class RepoPaths
    {
        /// <summary>
        /// The repo root: the first parent that has <c>schemas/event.schema.json</c>. Unity runs tests
        /// from unity/QALabSandbox, cs-check from tools/cs-check/bin/...; both are inside the repo.
        /// </summary>
        public static string Root
        {
            get
            {
                foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() })
                {
                    var dir = new DirectoryInfo(start);
                    while (dir != null)
                    {
                        if (File.Exists(Path.Combine(dir.FullName, "schemas", "event.schema.json")))
                        {
                            return dir.FullName;
                        }
                        dir = dir.Parent;
                    }
                }
                throw new DirectoryNotFoundException("could not find the repo root (schemas/event.schema.json)");
            }
        }

        public static string PathOf(params string[] parts) => Path.Combine(Root, Path.Combine(parts));

        public static string ReadText(params string[] parts) => File.ReadAllText(PathOf(parts));
    }

    public static class Json
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };

        public static JToken Parse(string text) => JsonConvert.DeserializeObject<JToken>(text, Settings);

        /// <summary>
        /// Compare parsed JSON, not bytes (spec 00 contract tests). Numbers compare by value, so
        /// <c>55</c> and <c>55.0</c> are equal, as they are for every JSON reader (Python included).
        /// </summary>
        public static void AssertSame(string expected, string actual, string because = "")
        {
            var a = Parse(expected);
            var b = Parse(actual);
            Assert.IsTrue(Equivalent(a, b), $"{because}\nexpected: {a.ToString(Formatting.None)}\nactual:   {b.ToString(Formatting.None)}");
        }

        public static bool Equivalent(JToken a, JToken b)
        {
            if (IsNumber(a) && IsNumber(b))
            {
                return System.Math.Abs(a.Value<double>() - b.Value<double>()) < 1e-9;
            }
            if (a.Type != b.Type)
            {
                return false;
            }
            switch (a)
            {
                case JObject objA:
                    var objB = (JObject)b;
                    if (objA.Count != objB.Count) return false;
                    foreach (var property in objA.Properties())
                    {
                        if (!objB.TryGetValue(property.Name, out var other) || !Equivalent(property.Value, other)) return false;
                    }
                    return true;
                case JArray arrA:
                    var arrB = (JArray)b;
                    if (arrA.Count != arrB.Count) return false;
                    for (var i = 0; i < arrA.Count; i++)
                    {
                        if (!Equivalent(arrA[i], arrB[i])) return false;
                    }
                    return true;
                default:
                    return JToken.DeepEquals(a, b);
            }
        }

        private static bool IsNumber(JToken t) => t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
    }

    /// <summary>A clock tests can set.</summary>
    public sealed class FakeClock : IClock
    {
        public double Seconds { get; set; }
        public DateTime UtcNow { get; set; } = new DateTime(2026, 10, 5, 10, 30, 0, DateTimeKind.Utc);
    }

    /// <summary>A main-thread snapshot tests can set. <see cref="OnFrameRead"/> lets a test pause a producer.</summary>
    public sealed class FakeState : IMainThreadState
    {
        private long _frame;

        public string Scene { get; set; } = "Sandbox_Level01";
        public float[] Position { get; set; }
        public Action OnFrameRead { get; set; }

        public long Frame
        {
            get
            {
                OnFrameRead?.Invoke();
                return _frame;
            }
            set => _frame = value;
        }
    }
}
