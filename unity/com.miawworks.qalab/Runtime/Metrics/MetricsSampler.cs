using System;
using UnityEngine.Profiling;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Once per second: fps, average and p95 frame time over that second, managed heap and total
    /// allocated memory. Called from the host's Update with the unscaled frame time; the ring buffer is
    /// preallocated, so steady state allocates nothing except the metric event itself.
    /// </summary>
    public sealed class MetricsSampler
    {
        private const double Megabyte = 1024.0 * 1024.0;
        private readonly EventWriter _writer;
        private readonly RingBuffer _frameMs = new RingBuffer(512);   // enough for 512 fps
        private float _elapsed;
        private int _frames;
        private bool _skipThisFrame;

        public MetricsSampler(EventWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        /// <summary>
        /// Leave the current frame out of the stats: QA Lab's own screenshot capture (M4) makes a frame
        /// slow, and counting it would report our observer effect as a game performance problem.
        /// </summary>
        public void ExcludeCurrentFrame() => _skipThisFrame = true;

        public void Tick(float unscaledDeltaTime)
        {
            _elapsed += unscaledDeltaTime;
            if (_skipThisFrame)
            {
                _skipThisFrame = false;
            }
            else
            {
                _frames++;
                _frameMs.Add(unscaledDeltaTime * 1000f);
            }
            if (_elapsed < 1f)
            {
                return;
            }
            if (_frames > 0)
            {
                var allocated = Profiler.GetTotalAllocatedMemoryLong();   // 0 in some release players
                _writer.Metric(
                    fps: _frames / _elapsed,
                    frameMs: _frameMs.Average(),
                    frameMsP95: _frameMs.Percentile(0.95f),
                    gcMb: GC.GetTotalMemory(false) / Megabyte,
                    memMb: allocated > 0 ? allocated / Megabyte : (double?)null);
            }
            _elapsed = 0f;
            _frames = 0;
            _frameMs.Clear();
        }
    }
}
