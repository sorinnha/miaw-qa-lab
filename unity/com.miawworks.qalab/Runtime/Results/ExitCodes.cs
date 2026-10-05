// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
namespace MiawWorks.QALab
{
    /// <summary>The player's exit code with <c>-qalabQuitOnEnd</c> (spec 01, "Results and exit codes").</summary>
    public static class ExitCodes
    {
        /// <summary>No blocker or critical detector fired.</summary>
        public const int Clean = 0;

        /// <summary>A blocker or critical detector fired (e.g. the player fell out of the world).</summary>
        public const int FatalDetector = 1;

        /// <summary>QA Lab itself failed (a detector, the bot or a capture threw).</summary>
        public const int InternalError = 2;

        /// <summary>
        /// An internal error wins over a fatal detector: if QA Lab broke, the run's findings are
        /// incomplete, and CI should look at the tool before trusting the game result.
        /// </summary>
        public static int For(bool fatalDetector, bool internalError)
        {
            if (internalError) return InternalError;
            return fatalDetector ? FatalDetector : Clean;
        }
    }
}
