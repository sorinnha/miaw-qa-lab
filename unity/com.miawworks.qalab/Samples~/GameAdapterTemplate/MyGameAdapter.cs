using System;
using System.Collections.Generic;
using MiawWorks.QALab;

namespace MyGame.QALab
{
    /// <summary>
    /// Template game adapter for a turn-based game (spec 01, "Game adapters"). Each step it asks
    /// <see cref="TurnPolicy.Decide"/> what to do, carries the decision out through
    /// <see cref="IGameCommands"/> and logs it as a repro step. Rename it and set <see cref="AdapterName"/>
    /// (snake_case, like <c>-qalabAdapter</c>). Run it with <c>-qalabAdapter my_game</c>; the bot runner that
    /// calls it arrives in QA Lab M4.
    /// </summary>
    public sealed class MyGameAdapter : IBotAdapter
    {
        public const string AdapterName = "my_game";

        private readonly Func<IGameCommands> _findGame;
        private int _ordersThisTurn;

        /// <param name="findGame">Returns the game's commands, or null while its scene isn't loaded.</param>
        public MyGameAdapter(Func<IGameCommands> findGame)
        {
            _findGame = findGame ?? throw new ArgumentNullException(nameof(findGame));
        }

        public string Name => AdapterName;

        public void Begin(BotContext ctx) => _ordersThisTurn = 0;

        public BotStepResult Step(BotContext ctx)
        {
            // Look the game up on every step, never cache it: after a scene reload the old turn manager is
            // destroyed, and a destroyed object behind an interface doesn't compare equal to null.
            var game = _findGame();
            var decision = TurnPolicy.Decide(game, ctx.Random, _ordersThisTurn);
            switch (decision.Kind)
            {
                case BotDecisionKind.EndTurn:
                    game.EndTurn();
                    _ordersThisTurn = 0;
                    ctx.LogAction("end_turn");
                    break;
                case BotDecisionKind.Order:
                    var accepted = game.Issue(decision.Unit, decision.Order);
                    _ordersThisTurn++;
                    ctx.LogAction("order", new Dictionary<string, object>
                    {
                        ["unit"] = decision.Unit, ["order"] = decision.Order, ["accepted"] = accepted,
                    });
                    break;
            }
            return BotStepResult.Continue;
        }

        public void End(BotContext ctx) => _ordersThisTurn = 0;
    }
}
