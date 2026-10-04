using System;
using System.Collections.Generic;
using MiawWorks.QALab;

namespace MyGame.QALab
{
    /// <summary>
    /// Template game adapter for a turn-based tactics game (spec 01, "Game adapters"): each step it picks a
    /// unit and one of its legal orders with the run's seeded RNG, issues it through
    /// <see cref="IGameCommands"/>, and logs it as a repro step. When no unit can act it ends the turn.
    /// Rename it, change <see cref="AdapterName"/>, and adapt the decision rule to your game.
    /// Run it with <c>-qalabAdapter my_game</c> (the bot runner that calls it arrives in QA Lab M4).
    /// </summary>
    public sealed class MyGameAdapter : IBotAdapter
    {
        public const string AdapterName = "my_game";

        // Safety valve: end the turn after this many orders even if units still report orders,
        // so a unit that never runs out of legal orders can't stall the run.
        private const int MaxOrdersPerTurn = 30;

        private readonly Func<IGameCommands> _findGame;
        private IGameCommands _game;
        private int _ordersThisTurn;

        /// <param name="findGame">Returns the game's commands, or null while its scene isn't loaded yet.</param>
        public MyGameAdapter(Func<IGameCommands> findGame)
        {
            _findGame = findGame ?? throw new ArgumentNullException(nameof(findGame));
        }

        public string Name => AdapterName;

        public void Begin(BotContext ctx)
        {
            _game = null;
            _ordersThisTurn = 0;
        }

        public BotStepResult Step(BotContext ctx)
        {
            _game ??= _findGame();   // the battle scene may load after the run starts
            if (_game == null || !_game.CanAct) return BotStepResult.Continue;   // menus, animations, enemy turn

            var units = _game.ActiveUnits();
            if (units.Count == 0 || _ordersThisTurn >= MaxOrdersPerTurn)
            {
                EndTurn(ctx);
                return BotStepResult.Continue;
            }

            // Only ctx.Random: the same -qalabSeed then makes the same choices (never UnityEngine.Random).
            var unit = ctx.Random.Pick(units);
            var orders = _game.LegalOrders(unit);
            if (orders.Count == 0)
            {
                EndTurn(ctx);
                return BotStepResult.Continue;
            }
            var order = ctx.Random.Pick(orders);
            var accepted = _game.Issue(unit, order);
            _ordersThisTurn++;
            ctx.LogAction("order", new Dictionary<string, object>
            {
                ["unit"] = unit, ["order"] = order, ["accepted"] = accepted,
            });
            return BotStepResult.Continue;
        }

        public void End(BotContext ctx) => _game = null;

        private void EndTurn(BotContext ctx)
        {
            _game.EndTurn();
            _ordersThisTurn = 0;
            ctx.LogAction("end_turn");
        }
    }
}
