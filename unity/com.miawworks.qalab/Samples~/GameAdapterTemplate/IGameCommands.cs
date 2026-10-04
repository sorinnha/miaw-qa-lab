using System.Collections.Generic;

namespace MyGame.QALab
{
    /// <summary>
    /// What the bot may do in your game, in your game's own words. Implement this on the script that
    /// already owns turns and orders (for a tactics game, the turn manager), so the adapter calls the same
    /// code a player's clicks call. That is the point of a game adapter: no fake input, no screen
    /// coordinates, and the action log reads like a tester's notes.
    /// <para>This interface fits a turn-based tactics game. For other genres, change it to your game's verbs
    /// (for example: <c>Destinations()</c>, <c>UseAbility(string)</c>, <c>OpenMenu(string)</c>).</para>
    /// </summary>
    public interface IGameCommands
    {
        /// <summary>True when the bot may act: the player's turn, no animation playing, no dialog open.</summary>
        bool CanAct { get; }

        /// <summary>Units that can still take an order this turn, by name as the game shows them.</summary>
        IReadOnlyList<string> ActiveUnits();

        /// <summary>Legal orders for one unit, as short readable strings: "move 3,4", "attack Orc_2", "wait".</summary>
        IReadOnlyList<string> LegalOrders(string unit);

        /// <summary>Give the order exactly as a player would. Returns false if the game refused it.</summary>
        bool Issue(string unit, string order);

        /// <summary>End the bot's turn.</summary>
        void EndTurn();
    }

    /// <summary>
    /// How the adapter finds your game: set it from your turn manager, e.g.
    /// <c>void Awake() { GameCommandsLocator.Current = this; }</c> and clear it in <c>OnDestroy</c>.
    /// </summary>
    public static class GameCommandsLocator
    {
        public static IGameCommands Current { get; set; }
    }
}
