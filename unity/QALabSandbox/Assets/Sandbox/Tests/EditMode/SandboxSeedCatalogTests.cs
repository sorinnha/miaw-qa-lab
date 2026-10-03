// Engine-free: runs in Unity's Test Runner and in tools/cs-check.
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MiawWorks.QALab;
using MiawWorks.QALab.Tests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace QALab.Sandbox.Tests
{
    /// <summary>The code catalog agrees with the design doc and with samples/sample_run/labels.json.</summary>
    public class SandboxSeedCatalogTests
    {
        // "## Enemy registry" → "Enemy registry"
        private static readonly Regex H2 = new Regex("^## (.+?)\\s*$", RegexOptions.Multiline);

        private static HashSet<string> DesignHeadings()
        {
            var headings = new HashSet<string>();
            foreach (Match m in H2.Matches(RepoPaths.ReadText("docs", "sandbox_design.md").Replace("\r", "")))
            {
                headings.Add(m.Groups[1].Value);
            }
            return headings;
        }

        private static JObject SampleEntry(string id)
        {
            var labels = Json.Parse(RepoPaths.ReadText("samples", "sample_run", "labels.json"));
            foreach (JObject bug in (JArray)labels["seeded_bugs"])
            {
                if ((string)bug["bug_id"] == id)
                {
                    bug.Remove("triggers");
                    return bug;
                }
            }
            return null;
        }

        private static string ToJson(SeedCatalogEntry entry) => JsonConvert.SerializeObject(entry);

        [Test]
        public void EveryEntryIsWellFormed()
        {
            var seen = new HashSet<string>();
            foreach (var entry in SandboxSeedCatalog.All())
            {
                StringAssert.IsMatch("^SB[0-9]{2}$", entry.BugId);
                Assert.IsTrue(seen.Add(entry.BugId), "duplicate " + entry.BugId);
                CollectionAssert.Contains(new[] { "log", "detector", "visual" }, entry.Type);
                StringAssert.IsMatch("^S[1-4]$", entry.ExpectedSeverity);
                Assert.IsTrue(entry.Match.HasAnyRule, entry.BugId + " needs at least one match rule");
                Assert.IsFalse(string.IsNullOrEmpty(entry.Title), entry.BugId);
            }
            Assert.GreaterOrEqual(seen.Count, 6);
        }

        [Test]
        public void EveryFeatureIsAnH2HeadingInTheDesignDoc()
        {
            var headings = DesignHeadings();
            foreach (var entry in SandboxSeedCatalog.All())
            {
                CollectionAssert.Contains(headings, entry.Feature, entry.BugId);
            }
        }

        [TestCase("SB01")]
        [TestCase("SB03")]
        [TestCase("SB04")]
        [TestCase("SB13")]
        [TestCase("SB14")]
        public void EntryMatchesTheSampleLabels(string id)
        {
            SeedCatalogEntry entry = null;
            foreach (var e in SandboxSeedCatalog.All()) if (e.BugId == id) entry = e;
            Assert.IsNotNull(entry, id);
            Json.AssertSame(SampleEntry(id).ToString(), ToJson(entry), id);
        }

        [Test]
        public void Sb05IsALogSeedMatchedByMessage()
        {
            var entry = SandboxSeedCatalog.SB05();
            Assert.AreEqual("Asset loading", entry.Feature);
            StringAssert.IsMatch(entry.Match.MessageRegex, "Failed to load asset Assets/Audio/sfx_17.wav");
        }

        [Test]
        public void LabelBookOutputForSampleTriggersMatchesTheSample()
        {
            var book = new LabelBook(SandboxSeedCatalog.All());
            var sample = Json.Parse(RepoPaths.ReadText("samples", "sample_run", "labels.json"));
            var expected = new JArray();
            foreach (JObject bug in (JArray)sample["seeded_bugs"])
            {
                var id = (string)bug["bug_id"];
                if (!book.IsKnown(id)) continue;   // M4 seeds and still-open YOU WRITE entries
                foreach (var trigger in (JArray)bug["triggers"])
                {
                    book.Trigger(id, (double)trigger["t"], (string)trigger["scene"]);
                }
                expected.Add(bug);
            }
            var actual = book.ToJson((string)sample["run_id"]);
            Json.AssertSame(expected.ToString(), actual["seeded_bugs"].ToString());
            Assert.GreaterOrEqual(expected.Count, 5);
        }

        [Test]
        public void StubbedEntriesAreReportedNotSilentlyDropped()
        {
            foreach (var id in SandboxSeedCatalog.Stubbed())
            {
                CollectionAssert.Contains(new[] { "SB02" }, id, "only YOU WRITE entries may be stubbed");
            }
        }

        [Test, Category("YouWrite")]
        public void Sb02EntryMatchesTheSampleLabels()
        {
            Json.AssertSame(SampleEntry("SB02").ToString(), ToJson(SandboxSeedCatalog.SB02()));
        }
    }
}
