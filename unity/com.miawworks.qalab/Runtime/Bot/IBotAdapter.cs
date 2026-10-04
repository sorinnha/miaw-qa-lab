namespace MiawWorks.QALab
{
    /// <summary>
    /// A bot strategy (spec 01, "Bot framework"): how the bot plays. The bot runner (M4) creates the
    /// adapter named by <c>-qalabAdapter</c> from <see cref="BotAdapterRegistry"/>, calls
    /// <see cref="Begin"/> once, <see cref="Step"/> every decision interval (0.25 s by default) until it
    /// returns <see cref="BotStepResult.Done"/> or the run's time is up, then <see cref="End"/>.
    /// Built-ins: NavMesh explorer and UI crawler (M4). Game adapters drive a game's own commands (M7);
    /// the package sample "Game adapter template" is a starting point.
    /// </summary>
    public interface IBotAdapter
    {
        /// <summary>The registry name; also written to run.json <c>adapter</c> and to every action event.</summary>
        string Name { get; }

        void Begin(BotContext ctx);

        /// <summary>Decide and issue one action. Use only <see cref="BotContext.Random"/> for choices.</summary>
        BotStepResult Step(BotContext ctx);

        void End(BotContext ctx);
    }

    /// <summary>What the runner does after a <see cref="IBotAdapter.Step"/>.</summary>
    public enum BotStepResult
    {
        /// <summary>Call Step again at the next decision interval.</summary>
        Continue,

        /// <summary>The adapter has nothing left to do: stop calling Step. The run still lasts its duration.</summary>
        Done,
    }
}
