// Engine-free (no UnityEngine): tools/cs-check also compiles this file and runs its tests
// (tools/cs-check/SampleTests/TurnPolicyTests.cs), so it can be checked without Unity.
using MiawWorks.QALab;

namespace MyGame.QALab
{
    public enum BotDecisionKind
    {
        /// <summary>Do nothing this step (no game yet, enemy turn, animation, dialog).</summary>
        Wait,
        /// <summary>Give <see cref="BotDecision.Order"/> to <see cref="BotDecision.Unit"/>.</summary>
        Order,
        /// <summary>End the bot's turn.</summary>
        EndTurn,
    }

    /// <summary>What the bot does at one decision step. <see cref="MyGameAdapter"/> carries it out and logs it.</summary>
    public readonly struct BotDecision
    {
        public BotDecision(BotDecisionKind kind, string unit = null, string order = null)
        {
            Kind = kind;
            Unit = unit;
            Order = order;
        }

        public BotDecisionKind Kind { get; }
        public string Unit { get; }
        public string Order { get; }

        public static BotDecision Wait() => new BotDecision(BotDecisionKind.Wait);
        public static BotDecision EndTurn() => new BotDecision(BotDecisionKind.EndTurn);
        public static BotDecision Issue(string unit, string order) => new BotDecision(BotDecisionKind.Order, unit, order);
    }

    /// <summary>The bot's decision rule for a turn-based game, kept free of Unity so it can be unit-tested.</summary>
    public static class TurnPolicy
    {
        /// <summary>
        /// Safety valve: end the turn after this many orders even if units still report orders, so a unit
        /// that never runs out of legal orders can't stall the run.
        /// </summary>
        public const int MaxOrdersPerTurn = 30;

        /// <summary>
        /// Learning task (M7), written by Claude at Sora's request (D-030): choose the bot's next move.
        /// Only decide here; never call <c>Issue</c> or <c>EndTurn</c> (the adapter does that, then logs it).
        /// <list type="number">
        /// <item>No game yet (<paramref name="game"/> is null) or <c>!game.CanAct</c>: <see cref="BotDecision.Wait"/>.</item>
        /// <item><paramref name="ordersThisTurn"/> has reached <see cref="MaxOrdersPerTurn"/>, or
        /// <c>game.ActiveUnits()</c> is empty: <see cref="BotDecision.EndTurn"/>.</item>
        /// <item>Otherwise pick a unit with <c>random.Pick</c>, then pick one of <c>game.LegalOrders(unit)</c>
        /// with <c>random.Pick</c>: <c>BotDecision.Issue(unit, order)</c>. A unit with no legal orders: end the turn.</item>
        /// </list>
        /// Use only <paramref name="random"/> (the run's seed), never <c>UnityEngine.Random</c> or a new
        /// <c>System.Random</c>, and pick the unit before the order: the tests replay the same seed in that order.
        /// </summary>
        public static BotDecision Decide(IGameCommands game, SeededRandom random, int ordersThisTurn)
        {
            if (game == null || !game.CanAct) return BotDecision.Wait();
            if (ordersThisTurn >= MaxOrdersPerTurn) return BotDecision.EndTurn();

            var units = game.ActiveUnits();
            if (units == null || units.Count == 0) return BotDecision.EndTurn();

            // Unit first, then order: the same seed must replay the same sequence of picks.
            string unit = random.Pick(units);
            var orders = game.LegalOrders(unit);
            if (orders == null || orders.Count == 0) return BotDecision.EndTurn();
            return BotDecision.Issue(unit, random.Pick(orders));
        }
    }
}
