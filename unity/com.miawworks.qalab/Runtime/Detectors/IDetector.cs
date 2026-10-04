// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// A rule that watches the game every frame and reports problems (spec 01, "Detectors"). The host
    /// calls <see cref="Tick"/> once per frame on the main thread with a snapshot of what it knows; a
    /// detector reports through <see cref="IDetectorReporter.Report"/>, which adds the position and scene,
    /// rate-limits, requests a screenshot and writes the event. Keep the rule in plain C# (no UnityEngine)
    /// so it can be unit-tested without a scene.
    /// </summary>
    public interface IDetector
    {
        /// <summary>snake_case name, written to <c>data.detector</c>.</summary>
        string Name { get; }

        void Tick(in DetectorFrame frame, IDetectorReporter reporter);
    }

    /// <summary>Where detectors send their findings (the <see cref="DetectorHub"/>).</summary>
    public interface IDetectorReporter
    {
        /// <summary>
        /// Write a <c>detector</c> event. Returns it, or null when the report was rate-limited or the run
        /// has ended (then nothing was written and no screenshot was taken).
        /// </summary>
        QAEvent Report(string detector, string severity, JObject details = null);
    }

    /// <summary>
    /// What the host knows about one frame, gathered on the main thread before detectors run. Plain values
    /// only, so detectors stay engine-free.
    /// </summary>
    public readonly struct DetectorFrame
    {
        public DetectorFrame(double t, long frame, float frameMs, bool captureFrame, float[] playerPos,
            bool botMoving, int newExceptions)
        {
            T = t;
            Frame = frame;
            FrameMs = frameMs;
            CaptureFrame = captureFrame;
            PlayerPos = playerPos;
            BotMoving = botMoving;
            NewExceptions = newExceptions;
        }

        /// <summary>Seconds since the run started (the events' <c>t</c>).</summary>
        public double T { get; }

        public long Frame { get; }

        /// <summary>This frame's unscaled duration in milliseconds.</summary>
        public float FrameMs { get; }

        /// <summary>
        /// True when this frame's time includes QA Lab's own screenshot capture (taken at the end of the
        /// previous frame). Such frames are slow because of us, not the game: perf rules skip them.
        /// </summary>
        public bool CaptureFrame { get; }

        /// <summary>The registered player's position (x, y, z), or null when no player is registered.</summary>
        public float[] PlayerPos { get; }

        /// <summary>True while the bot is asking the player to move (a move target is set).</summary>
        public bool BotMoving { get; }

        /// <summary>Exception events written since the previous frame.</summary>
        public int NewExceptions { get; }
    }
}
