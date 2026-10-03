using System;
using System.IO;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    public class LabelBookTests
    {
        private static SeedCatalogEntry Entry(string id, string feature = "Doors") => new SeedCatalogEntry
        {
            BugId = id, Type = "log", Title = "t " + id, Feature = feature, ExpectedSeverity = "S2",
            Match = new MatchRule { StackContains = "X." + id },
        };

        [Test]
        public void OnlyTriggeredSeedsAreListedSortedWithTheirTriggers()
        {
            var book = new LabelBook(new[] { Entry("SB04"), Entry("SB01"), Entry("SB13") });
            book.Trigger("SB04", 19.0512, "Sandbox_Level01");
            book.Trigger("SB01", 8.21, "Sandbox_Level01");
            book.Trigger("SB04", 29.0, null);
            var json = book.ToJson("run-7");

            Assert.AreEqual("qalab.labels/1", (string)json["schema"]);
            Assert.AreEqual("run-7", (string)json["run_id"]);
            Assert.AreEqual(0, ((JArray)json["screenshots"]).Count);
            var bugs = (JArray)json["seeded_bugs"];
            Assert.AreEqual(2, bugs.Count, "SB13 never fired");
            Assert.AreEqual("SB01", (string)bugs[0]["bug_id"]);
            Assert.AreEqual("SB04", (string)bugs[1]["bug_id"]);
            var triggers = (JArray)bugs[1]["triggers"];
            Assert.AreEqual(19.051, (double)triggers[0]["t"]);
            Assert.AreEqual("Sandbox_Level01", (string)triggers[0]["scene"]);
            Assert.IsNull(triggers[1]["scene"], "scene is omitted when unknown");
            Assert.AreEqual("X.SB04", (string)bugs[1]["match"]["stack_contains"]);
            Assert.IsNull(bugs[1]["match"]["message_regex"], "unset rules are omitted");
        }

        [Test]
        public void UnknownIdsAreRecordedButNotWritten()
        {
            var book = new LabelBook(new[] { Entry("SB01") });
            book.Trigger("SB02", 1.0, "s");
            Assert.IsFalse(book.IsKnown("SB02"));
            CollectionAssert.AreEqual(new[] { "SB02" }, book.UnknownTriggers);
            Assert.AreEqual(0, ((JArray)book.ToJson("r")["seeded_bugs"]).Count);
        }

        [Test]
        public void DuplicateCatalogIdsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => new LabelBook(new[] { Entry("SB01"), Entry("SB01") }));
        }

        [Test]
        public void ToJsonCanBeCalledTwice()
        {
            var book = new LabelBook(new[] { Entry("SB01") });
            book.Trigger("SB01", 1.0, "s");
            Json.AssertSame(book.ToJson("r").ToString(), book.ToJson("r").ToString());
        }

        [Test]
        public void WriteToProducesLfJsonWithoutBom()
        {
            var path = Path.Combine(Path.GetTempPath(), "qalab-labels-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                var book = new LabelBook(new[] { Entry("SB01") });
                book.Trigger("SB01", 2.5, "Sandbox_Level01");
                book.WriteTo(path, "r");
                var bytes = File.ReadAllBytes(path);
                Assert.AreNotEqual(0xEF, bytes[0]);
                CollectionAssert.DoesNotContain(bytes, (byte)'\r');
                Assert.AreEqual("SB01", (string)Json.Parse(File.ReadAllText(path))["seeded_bugs"][0]["bug_id"]);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Test]
        public void MatchRuleKnowsWhetherAnyRuleIsSet()
        {
            Assert.IsFalse(new MatchRule().HasAnyRule);
            Assert.IsTrue(new MatchRule { Radius = 4f }.HasAnyRule);
        }
    }
}
