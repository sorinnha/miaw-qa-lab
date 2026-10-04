// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Every detector event goes through here (spec 01): <see cref="Report"/> rate-limits (same detector,
    /// same 4 m cell, once per 10 s), reserves a screenshot, writes the event (the writer adds pos and
    /// scene from the main-thread cache) and keeps the counts that results.xml and the exit code use.
    /// <see cref="Tick"/> runs the registered detectors once per frame. A detector that throws is turned
    /// off for the rest of the run instead of breaking the game: <see cref="NotImplementedException"/>
    /// means "not written yet" (a YOU WRITE stub) and is only reported; anything else counts as a
    /// QA Lab internal error (exit code 2). Main thread only.
    /// </summary>
    public sealed class DetectorHub : IDetectorReporter
    {
        private readonly EventWriter _writer;
        private readonly IClock _clock;
        private readonly IMainThreadState _state;
        private readonly RateLimiter _limiter;
        private readonly Func<string> _requestScreenshot;
        private readonly Action<string> _warn;
        private readonly List<IDetector> _detectors = new List<IDetector>();
        private readonly Dictionary<string, int> _counts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _worst = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _skipped = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _failed = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <param name="requestScreenshot">Reserves a screenshot taken at the end of this frame and returns
        /// its run-relative path (<c>shots/000003.png</c>), or null when screenshots are off.</param>
        /// <param name="warn">Where QA Lab's own warnings go (the host passes a <c>[QALab]</c> logger).</param>
        public DetectorHub(EventWriter writer, IClock clock, IMainThreadState state, RateLimiter limiter = null,
            Func<string> requestScreenshot = null, Action<string> warn = null)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _limiter = limiter ?? new RateLimiter();
            _requestScreenshot = requestScreenshot;
            _warn = warn ?? (_ => { });
        }

        /// <summary>Detectors in the order they run.</summary>
        public IReadOnlyList<IDetector> Detectors => _detectors;

        /// <summary>Detector events written, per detector name.</summary>
        public IReadOnlyDictionary<string, int> Counts => _counts;

        /// <summary>Detectors that aren't written yet (they threw <see cref="NotImplementedException"/>), with the reason.</summary>
        public IReadOnlyDictionary<string, string> Skipped => _skipped;

        /// <summary>Detectors that threw anything else, with the error: QA Lab internal errors.</summary>
        public IReadOnlyDictionary<string, string> Failed => _failed;

        /// <summary>True if <paramref name="detector"/> was turned off during the run.</summary>
        public bool IsOff(string detector) => _skipped.ContainsKey(detector) || _failed.ContainsKey(detector);

        /// <summary>True once a blocker or critical detector event was written (exit code 1).</summary>
        public bool FatalFired { get; private set; }

        public void Add(IDetector detector)
        {
            if (detector == null) throw new ArgumentNullException(nameof(detector));
            if (!DetectorNames.IsValid(detector.Name))
            {
                throw new ArgumentException($"bad detector name '{detector.Name}'", nameof(detector));
            }
            foreach (var existing in _detectors)
            {
                if (existing.Name == detector.Name) throw new ArgumentException($"detector '{detector.Name}' is already registered");
            }
            _detectors.Add(detector);
        }

        /// <summary>Events written by <paramref name="detector"/> so far.</summary>
        public int Count(string detector) => _counts.TryGetValue(detector, out var n) ? n : 0;

        /// <summary>The worst severity <paramref name="detector"/> reported, or null.</summary>
        public string WorstSeverity(string detector) => _worst.TryGetValue(detector, out var s) ? s : null;

        /// <summary>Run every enabled detector on this frame.</summary>
        public void Tick(in DetectorFrame frame)
        {
            foreach (var detector in _detectors)
            {
                if (IsOff(detector.Name)) continue;
                try
                {
                    detector.Tick(in frame, this);
                }
                catch (NotImplementedException)
                {
                    _skipped[detector.Name] = "not implemented yet";
                    _warn($"detector '{detector.Name}' is not implemented yet and is off for this run");
                }
                catch (Exception exc)
                {
                    _failed[detector.Name] = exc.GetType().Name + ": " + exc.Message;
                    _warn($"detector '{detector.Name}' failed and is off for this run: {_failed[detector.Name]}");
                }
            }
        }

        /// <inheritdoc />
        public QAEvent Report(string detector, string severity, JObject details = null)
        {
            if (!DetectorNames.IsValid(detector)) throw new ArgumentException($"bad detector name '{detector}'", nameof(detector));
            if (!DetectorSeverity.IsKnown(severity)) throw new ArgumentException($"bad severity '{severity}'", nameof(severity));
            if (!_limiter.Allow(detector, _state.Position, _clock.Seconds))
            {
                return null;
            }
            var data = new JObject { ["detector"] = detector, ["severity"] = severity };
            if (details != null) data["details"] = details;
            var shot = _requestScreenshot?.Invoke();
            if (shot != null) data["screenshot"] = shot;
            var written = _writer.Enqueue(EventKinds.Detector, data);
            if (written == null)
            {
                return null;   // the run has ended
            }
            _counts[detector] = Count(detector) + 1;
            _worst[detector] = DetectorSeverity.Worst(WorstSeverity(detector), severity);
            if (DetectorSeverity.IsFatal(severity)) FatalFired = true;
            return written;
        }
    }
}
