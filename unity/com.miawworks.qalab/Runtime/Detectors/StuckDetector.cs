// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// <c>stuck</c> (spec 01, severity major): wires <see cref="StuckCalculator"/> into the detector hub.
    /// The host calls <see cref="Tick"/> once per frame. Until it is written, the hub turns this detector
    /// off for the run (it throws <see cref="NotImplementedException"/>) and everything else keeps working.
    /// </summary>
    public sealed class StuckDetector : IDetector
    {
        private readonly StuckCalculator _calculator;

        public StuckDetector(StuckCalculator calculator = null)
        {
            _calculator = calculator ?? new StuckCalculator();
        }

        public string Name => DetectorNames.Stuck;

        /// <summary>
        /// YOU WRITE (M4). Each frame:
        /// <list type="number">
        /// <item>No player (<c>frame.PlayerPos == null</c>): treat it as "not moving".</item>
        /// <item>Feed the calculator: time <c>frame.T</c>, x = <c>PlayerPos[0]</c>, z = <c>PlayerPos[2]</c>,
        /// moving = <c>frame.BotMoving</c>.</item>
        /// <item>When it says stuck, call <c>reporter.Report(Name, DetectorSeverity.Major, details)</c> with
        /// details <c>{"window_s": WindowS, "min_distance_m": MinDistanceM}</c>, then reset the calculator,
        /// so a player stuck for 12 s needs a new full window before the next report.</item>
        /// </list>
        /// Use the <see cref="DetectorSeverity"/> and <see cref="DetectorNames"/> constants, not string literals.
        /// </summary>
        public void Tick(in DetectorFrame frame, IDetectorReporter reporter) => throw new NotImplementedException("YOU WRITE");
    }
}
