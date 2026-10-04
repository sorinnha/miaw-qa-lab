// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// <c>perf_spike</c> (spec 01): a frame longer than 50 ms, unless QA Lab's own screenshot made it slow.
    /// Loading a scene also makes a few frames slow, and that's not a gameplay hitch, so the host calls
    /// <see cref="Suppress"/> on every scene load.
    /// </summary>
    public sealed class PerfSpikeDetector : IDetector
    {
        public const float DefaultThresholdMs = 50f;
        public const int DefaultSceneLoadGraceFrames = 10;

        private int _suppressFrames;

        public PerfSpikeDetector(float thresholdMs = DefaultThresholdMs)
        {
            if (!(thresholdMs > 0f)) throw new ArgumentOutOfRangeException(nameof(thresholdMs));
            ThresholdMs = thresholdMs;
        }

        public string Name => DetectorNames.PerfSpike;

        public float ThresholdMs { get; }

        /// <summary>Ignore the next <paramref name="frames"/> frames (scene loads, the first frames of a run).</summary>
        public void Suppress(int frames = DefaultSceneLoadGraceFrames) => _suppressFrames = Math.Max(_suppressFrames, frames);

        public void Tick(in DetectorFrame frame, IDetectorReporter reporter)
        {
            if (_suppressFrames > 0)
            {
                _suppressFrames--;
                return;
            }
            if (frame.CaptureFrame || frame.FrameMs <= ThresholdMs)
            {
                return;
            }
            reporter.Report(Name, DetectorSeverity.Minor, new JObject
            {
                ["frame_ms"] = Math.Round(frame.FrameMs, 1),
                ["threshold_ms"] = ThresholdMs,
            });
        }
    }
}
