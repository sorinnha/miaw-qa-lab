// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// <c>exception_burst</c> (spec 01): more than 20 exception events within 1 s. One burst is one event;
    /// the window starts empty again after a report, so a game that keeps throwing reports again only
    /// after another 21 exceptions (and the hub's rate limit still applies).
    /// </summary>
    public sealed class ExceptionBurstDetector : IDetector
    {
        private readonly ExceptionBurstCounter _counter;

        public ExceptionBurstDetector(ExceptionBurstCounter counter = null)
        {
            _counter = counter ?? new ExceptionBurstCounter();
        }

        public string Name => DetectorNames.ExceptionBurst;

        public void Tick(in DetectorFrame frame, IDetectorReporter reporter)
        {
            var total = _counter.Add(frame.T, frame.NewExceptions);
            if (!_counter.IsBurst) return;
            _counter.Reset();
            reporter.Report(Name, DetectorSeverity.Major, new JObject
            {
                ["exceptions"] = total,
                ["window_s"] = _counter.WindowS,
                ["threshold"] = _counter.Threshold,
            });
        }
    }
}
