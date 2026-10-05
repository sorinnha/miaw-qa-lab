using System.Collections.Generic;
using System.Linq;
using MiawWorks.QALab;
using MyGame.QALab;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    /// <summary>
    /// The game adapter template's decision rule, <c>TurnPolicy.Decide</c> (an M7 learning task, written
    /// by Claude at Sora's request, D-030)
    /// (unity/com.miawworks.qalab/Samples~/GameAdapterTemplate/TurnPolicy.cs). These tests live in
    /// cs-check only: the sample is copied into a game, where its own tests belong.
    /// Run: dotnet test tools/cs-check -c Release --filter "FullyQualifiedName~TurnPolicy"
    /// </summary>
    public class TurnPolicyTests
    {
        /// <summary>A tactics game in memory: units and their legal orders; it counts what was called.</summary>
        private sealed class FakeGame : IGameCommands
        {
            public bool CanAct { get; set; } = true;
            public Dictionary<string, List<string>> Orders { get; } = new Dictionary<string, List<string>>();
            public int Issued { get; private set; }
            public int TurnsEnded { get; private set; }
            public int UnitQueries { get; private set; }

            public IReadOnlyList<string> ActiveUnits()
            {
                UnitQueries++;
                return Orders.Keys.ToList();
            }

            public IReadOnlyList<string> LegalOrders(string unit) => Orders[unit];

            public bool Issue(string unit, string order)
            {
                Issued++;
                return true;
            }

            public void EndTurn() => TurnsEnded++;
        }

        private static FakeGame ThreeUnits() => new FakeGame
        {
            Orders =
            {
                ["Archer_1"] = new List<string> { "move 3,4", "attack Orc_2", "wait" },
                ["Knight_1"] = new List<string> { "move 5,5", "wait" },
                ["Mage_1"] = new List<string> { "cast fire Orc_2", "move 1,2", "wait" },
            },
        };

        [Test]
        public void WaitsWithoutAGameOrWhileTheGameCantAct()
        {
            Assert.AreEqual(BotDecisionKind.Wait, TurnPolicy.Decide(null, new SeededRandom(1), 0).Kind);
            var busy = ThreeUnits();
            busy.CanAct = false;
            Assert.AreEqual(BotDecisionKind.Wait, TurnPolicy.Decide(busy, new SeededRandom(1), 0).Kind);
        }

        [Test]
        public void EndsTheTurnWhenNoUnitCanAct()
        {
            Assert.AreEqual(BotDecisionKind.EndTurn, TurnPolicy.Decide(new FakeGame(), new SeededRandom(1), 0).Kind);
        }

        [Test]
        public void EndsTheTurnAtTheSafetyCap()
        {
            var decision = TurnPolicy.Decide(ThreeUnits(), new SeededRandom(1), TurnPolicy.MaxOrdersPerTurn);
            Assert.AreEqual(BotDecisionKind.EndTurn, decision.Kind);
        }

        [Test]
        public void AUnitWithoutLegalOrdersEndsTheTurn()
        {
            var stuck = new FakeGame { Orders = { ["Archer_1"] = new List<string>() } };
            Assert.AreEqual(BotDecisionKind.EndTurn, TurnPolicy.Decide(stuck, new SeededRandom(1), 0).Kind);
        }

        [TestCase(1)]
        [TestCase(7)]
        [TestCase(42)]
        public void PicksTheUnitThenTheOrderWithTheRunsSeed(int seed)
        {
            var game = ThreeUnits();
            var replay = new SeededRandom(seed);
            var expectedUnit = replay.Pick(game.Orders.Keys.ToList());
            var expectedOrder = replay.Pick(game.Orders[expectedUnit]);

            var decision = TurnPolicy.Decide(game, new SeededRandom(seed), 0);

            Assert.AreEqual(BotDecisionKind.Order, decision.Kind);
            Assert.AreEqual(expectedUnit, decision.Unit);
            Assert.AreEqual(expectedOrder, decision.Order);
        }

        [Test]
        public void OnlyDecidesTheAdapterCarriesItOut()
        {
            var game = ThreeUnits();
            TurnPolicy.Decide(game, new SeededRandom(3), 0);
            TurnPolicy.Decide(new FakeGame(), new SeededRandom(3), 0);
            Assert.AreEqual(0, game.Issued, "Decide must not call Issue");
            Assert.AreEqual(0, game.TurnsEnded, "Decide must not call EndTurn");
        }

        [Test]
        public void SameSeedGivesTheSameSequenceOfDecisions()
        {
            List<string> Run(int seed)
            {
                var random = new SeededRandom(seed);
                var game = ThreeUnits();
                return Enumerable.Range(0, 20)
                    .Select(i => TurnPolicy.Decide(game, random, i % 5))
                    .Select(d => $"{d.Kind} {d.Unit} {d.Order}")
                    .ToList();
            }

            CollectionAssert.AreEqual(Run(11), Run(11));
            CollectionAssert.AreNotEqual(Run(11), Run(12));
        }
    }
}
