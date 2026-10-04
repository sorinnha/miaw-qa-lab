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

        // One log event per seed as the sandbox scripts produce it in a real run: each stack is the call
        // path in the code, outermost frame last, with Unity's "(at file:line)" parts left out. Unlike
        // the hand-made sample run, these stacks are complete (the sample's SB03 stops at AssignTarget).
        private static readonly (string Id, string Message, string Stack)[] RealRunEvents =
        {
            ("SB01", "NullReferenceException: Object reference not set to an instance of an object",
                "QALab.Sandbox.SeededDoor.Open ()\nQALab.Sandbox.Interactor.TryInteract (UnityEngine.GameObject target)\nQALab.Sandbox.Interactor.Update ()"),
            ("SB02", "Inventory slot 7 out of range (size 5)",
                "UnityEngine.Debug:LogError (object)\nQALab.Sandbox.SeededInventory:GetSlot (int)\nQALab.Sandbox.HudInventory:Refresh ()\nQALab.Sandbox.HudInventory:Update ()"),
            ("SB03", "KeyNotFoundException: The given key 'enemy_4f2a9c1e' was not present in the dictionary.",
                "System.Collections.Generic.Dictionary`2[TKey,TValue].get_Item (TKey key)\nQALab.Sandbox.SeededEnemyRegistry.Get (System.String id)\nQALab.Sandbox.SeededSpawner.AssignTarget (QALab.Sandbox.Enemy enemy)\nQALab.Sandbox.SeededSpawner.Update ()"),
            ("SB04", "NullReferenceException: Object reference not set to an instance of an object",
                "QALab.Sandbox.SeededSpawner.SpawnWave ()\nQALab.Sandbox.SeededSpawner.Update ()"),
            ("SB05", "Failed to load asset Assets/Audio/sfx_17.wav",
                "UnityEngine.Debug:LogError (object)\nQALab.Sandbox.AssetLoader:Load (string)\nQALab.Sandbox.AssetLoader:Update ()"),
            ("SB13", "Footstep audio clip missing for surface 'Gravel'",
                "UnityEngine.Debug:LogWarning (object)\nQALab.Sandbox.FootstepAudio:Play (string)\nQALab.Sandbox.SandboxPlayer:OnStep ()\nQALab.Sandbox.SandboxPlayer:Update ()"),
            ("SB14", "Damage: division by zero in SafeDivide",
                "UnityEngine.Debug:LogError (object)\nQALab.Sandbox.MathUtil:SafeDivide (single,single,string)\nQALab.Sandbox.DamageCalculator:Compute (QALab.Sandbox.Weapon)\nQALab.Sandbox.CombatDriver:Update ()"),
            ("SB14", "Movement speed: division by zero in SafeDivide",
                "UnityEngine.Debug:LogError (object)\nQALab.Sandbox.MathUtil:SafeDivide (single,single,string)\nQALab.Sandbox.SpeedModel:GetSpeed ()\nQALab.Sandbox.CombatDriver:Update ()"),
        };

        // Spec 00 "match": every key given must match the event (stack_contains = substring of the
        // stack, message_regex = regex search on the message). Log seeds use only these two keys.
        private static bool Matches(MatchRule rule, string message, string stack)
        {
            if (rule.StackContains != null && (stack == null || !stack.Contains(rule.StackContains))) return false;
            if (rule.MessageRegex != null && !Regex.IsMatch(message, rule.MessageRegex)) return false;
            return rule.StackContains != null || rule.MessageRegex != null;
        }

        [Test]
        public void EachRealRunEventMatchesOnlyItsOwnSeed()
        {
            foreach (var e in RealRunEvents)
            {
                var matched = new List<string>();
                foreach (var entry in SandboxSeedCatalog.All())
                {
                    if (Matches(entry.Match, e.Message, e.Stack)) matched.Add(entry.BugId);
                }
                // A seed without a catalog entry (SB02 until its YOU WRITE entry exists) matches nothing.
                var expected = new List<string>();
                foreach (var entry in SandboxSeedCatalog.All()) if (entry.BugId == e.Id) expected.Add(e.Id);
                CollectionAssert.AreEqual(expected, matched, e.Id + ": " + e.Message);
            }
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
