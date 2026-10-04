// Engine-free: runs in Unity's Test Runner and in tools/cs-check.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MiawWorks.QALab;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    /// <summary>Spec 01 bot framework: SeededRandom is deterministic, the adapter registry, action data.</summary>
    public class BotFrameworkTests
    {
        private static List<int> Draws(SeededRandom random, int count) =>
            Enumerable.Range(0, count).Select(_ => random.Next(1000)).ToList();

        [Test]
        public void SameSeedGivesTheSameDecisions()
        {
            CollectionAssert.AreEqual(Draws(new SeededRandom(42), 50), Draws(new SeededRandom(42), 50));
            CollectionAssert.AreNotEqual(Draws(new SeededRandom(42), 50), Draws(new SeededRandom(43), 50));
            Assert.AreEqual(42, new SeededRandom(42).Seed);
        }

        [Test]
        public void RangesStayInBoundsAndChanceHonoursItsEdges()
        {
            var random = new SeededRandom(7);
            for (var i = 0; i < 1000; i++)
            {
                var f = random.Range(-2f, 3f);
                Assert.That(f, Is.GreaterThanOrEqualTo(-2f).And.LessThan(3f));
                Assert.That(random.Range(5, 8), Is.InRange(5, 7));
                Assert.IsFalse(random.Chance(0));
                Assert.IsTrue(random.Chance(1));
            }
        }

        [Test]
        public void PickChoosesFromTheListAndRefusesAnEmptyOne()
        {
            var random = new SeededRandom(1);
            var items = new[] { "a", "b", "c" };
            var picked = new HashSet<string>();
            for (var i = 0; i < 100; i++) picked.Add(random.Pick(items));
            CollectionAssert.AreEquivalent(items, picked);
            Assert.Throws<ArgumentException>(() => random.Pick(new string[0]));
            Assert.Throws<ArgumentException>(() => random.Pick<string>(null));
        }

        private sealed class Dummy
        {
            public string Label;
        }

        private static NamedRegistry<Dummy> AdapterRegistry() => new NamedRegistry<Dummy>(CommandLine.IsAdapterName);

        [Test]
        public void RegistryCreatesByNameAndLaterRegistrationsWin()
        {
            var registry = AdapterRegistry();
            registry.Register("my_game", () => new Dummy { Label = "first" });
            Assert.IsTrue(registry.IsRegistered("my_game"));
            Assert.AreEqual("first", registry.Create("my_game").Label);
            Assert.AreNotSame(registry.Create("my_game"), registry.Create("my_game"), "a new adapter per run");

            registry.Register("my_game", () => new Dummy { Label = "override" });
            Assert.AreEqual("override", registry.Create("my_game").Label);
            registry.Register("crimson_tactics", () => new Dummy());
            CollectionAssert.AreEqual(new[] { "crimson_tactics", "my_game" }, registry.Names);
        }

        [Test]
        public void EveryRegisteredAdapterNameIsAcceptedByTheCommandLine()
        {
            var registry = AdapterRegistry();
            registry.Register("crimson_tactics", () => new Dummy());
            var errors = new List<string>();
            var options = CommandLine.Parse(new[] { "-qalab", "-qalabAdapter", registry.Names[0] }, errors);
            Assert.IsEmpty(errors);
            Assert.AreEqual("crimson_tactics", options.Adapter);
        }

        [TestCase("my game")]
        [TestCase("crimson-tactics")]
        [TestCase("My_Game")]
        [TestCase("")]
        public void RegistryRejectsNamesTheCommandLineWouldReject(string name)
        {
            Assert.Throws<ArgumentException>(() => AdapterRegistry().Register(name, () => new Dummy()));
            var errors = new List<string>();
            CommandLine.Parse(new[] { "-qalab", "-qalabAdapter", name }, errors);
            Assert.IsNotEmpty(errors, "the command line rejects it too");
        }

        [Test]
        public void RegistryReturnsNullForUnknownNamesAndNeedsAFactory()
        {
            var registry = AdapterRegistry();
            Assert.IsNull(registry.Create("nothing"));
            Assert.IsNull(registry.Create(null));
            Assert.IsFalse(registry.IsRegistered(null));
            Assert.Throws<ArgumentNullException>(() => registry.Register("ok", null));
            Assert.Throws<ArgumentNullException>(() => new NamedRegistry<Dummy>(null));
        }

        private static JObject ActionExample()
        {
            foreach (var line in RepoPaths.ReadText("schemas", "examples", "events_valid.jsonl").Split('\n'))
            {
                if (line.Trim().Length == 0) continue;
                var e = (JObject)Json.Parse(line);
                if ((string)e["kind"] == "action") return e;
            }
            throw new InvalidOperationException("no action example");
        }

        [Test]
        public void ActionDataMatchesTheSchemaExample()
        {
            var example = ActionExample();
            var d = example["data"];
            var built = BotActionData.Build((string)d["action"], (int)d["step"], (string)d["adapter"],
                d["target"].ToObject<float[]>());
            Json.AssertSame(d.ToString(), built.ToString());
        }

        [Test]
        public void ActionDataWritesOnlyTheFieldsGivenAndRoundsTheTarget()
        {
            var turn = BotActionData.Build("end_turn", 3, "my_game");
            Json.AssertSame("{\"action\":\"end_turn\",\"step\":3,\"adapter\":\"my_game\"}", turn.ToString());

            var click = BotActionData.Build("click", 4, "ui_crawler", uiPath: "Canvas/Settings/Apply");
            Assert.AreEqual("Canvas/Settings/Apply", (string)click["ui_path"]);
            Assert.IsNull(click["target"]);

            var move = BotActionData.Build("move", 5, "my_game", new[] { 1.23456f, 0f, -7.891f },
                args: new JObject { ["unit"] = "archer_1", ["to"] = "3,4" });
            CollectionAssert.AreEqual(new[] { 1.23, 0.0, -7.89 }, move["target"].ToObject<double[]>().Select(v => Math.Round(v, 2)));
            Assert.AreEqual("archer_1", (string)move["args"]["unit"]);

            Assert.Throws<ArgumentException>(() => BotActionData.Build("", 1, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => BotActionData.Build("move", -1, "x"));
            Assert.Throws<ArgumentException>(() => BotActionData.Build("move", 1, "x", new[] { 1f, 2f }));
        }

        [Test]
        public void ActionEventThroughTheWriterIsSchemaShaped()
        {
            var output = new StringWriter();
            using (var writer = new EventWriter("r", new FakeClock(), new FakeState(), output))
            {
                writer.Enqueue(EventKinds.Action, BotActionData.Build("interact", 1, "navmesh_explorer",
                    args: new JObject { ["object"] = "Door_02" }));
            }
            var e = (JObject)Json.Parse(output.ToString().Trim());
            Assert.AreEqual("action", (string)e["kind"]);
            Assert.IsFalse(e.ContainsKey("stack"));
            CollectionAssert.AreEquivalent(new[] { "action", "step", "adapter", "args" },
                ((JObject)e["data"]).Properties().Select(p => p.Name));
        }
    }
}
