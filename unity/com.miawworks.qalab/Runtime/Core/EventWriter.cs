// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Thread-safe producer of <c>events.jsonl</c>.
    /// <list type="bullet">
    /// <item>Any thread may call the <c>Log</c>/<c>Marker</c>/<c>Metric</c>/<c>Enqueue</c> methods: they
    /// stamp <c>seq</c> (<see cref="Interlocked.Increment(ref long)"/>), <c>t</c> (Stopwatch) and the
    /// main-thread snapshot, then push onto a <see cref="ConcurrentQueue{T}"/>.</item>
    /// <item>Only the main thread calls <see cref="Drain"/> (every 0.5 s and on quit): it sorts the batch
    /// by <c>seq</c> and appends one line per event. An event created on another thread just before a
    /// drain can land in the next batch, so the file may be slightly out of order; readers sort by seq.</item>
    /// <item>The main thread ends the run with <see cref="Close"/>: producers from then on get null and
    /// take no seq, producers already past that check finish enqueuing first, and the closing marker
    /// (<c>run_end</c>) gets the last seq. So the file has no seq gaps and nothing after <c>run_end</c>.</item>
    /// </list>
    /// </summary>
    public sealed class EventWriter : IDisposable
    {
        private readonly string _runId;
        private readonly IClock _clock;
        private readonly IMainThreadState _state;
        private readonly TextWriter _out;
        private readonly ConcurrentQueue<QAEvent> _queue = new ConcurrentQueue<QAEvent>();
        private readonly List<QAEvent> _batch = new List<QAEvent>(256);
        private static readonly Comparison<QAEvent> BySeq = (a, b) => a.Seq.CompareTo(b.Seq);
        private long _lastSeq = -1;   // Interlocked.Increment makes the first seq 0
        private long _written;
        private int _sealed;       // 1 once Close/Dispose has begun: producers drop new events
        private int _inFlight;     // producers between their sealed check and their enqueue
        private bool _closed;      // set after the file is closed; main thread only

        public EventWriter(string runId, IClock clock, IMainThreadState state, TextWriter output)
        {
            _runId = runId ?? throw new ArgumentNullException(nameof(runId));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _out = output ?? throw new ArgumentNullException(nameof(output));
        }

        /// <summary>Append to <c>&lt;runDir&gt;/events.jsonl</c> as UTF-8 without BOM, LF line endings.</summary>
        public static EventWriter ToFile(string runId, IClock clock, IMainThreadState state, string eventsPath)
        {
            var stream = new FileStream(eventsPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\n", AutoFlush = false };
            return new EventWriter(runId, clock, state, writer);
        }

        /// <summary>How many events have been written so far.</summary>
        public long Written => Interlocked.Read(ref _written);

        /// <summary>Events created but not yet written.</summary>
        public int Pending => _queue.Count;

        /// <summary>
        /// Longest time <see cref="Close"/> waits for producers in flight. They never block, so they
        /// finish within microseconds; the cap only matters for a thread frozen mid-event (a debugger
        /// breakpoint), whose event is then lost and leaves a seq gap.
        /// </summary>
        public TimeSpan CloseWait { get; set; } = TimeSpan.FromSeconds(1);

        // ---- producers (any thread) ---------------------------------------------------------------

        /// <summary>
        /// Stamp and enqueue an event of any kind. <c>data</c> may be null for log events. Once the writer
        /// is closing this returns null and takes no seq.
        /// </summary>
        public QAEvent Enqueue(string kind, JObject data = null, string level = null, string message = null, string stack = null)
        {
            // The in-flight bracket is what Close waits on. Both sides use full fences (Interlocked) before
            // reading the other side's flag, so either Close sees this producer in flight and waits for its
            // enqueue, or this producer sees _sealed and drops the event before taking a seq.
            Interlocked.Increment(ref _inFlight);
            try
            {
                if (Volatile.Read(ref _sealed) != 0)
                {
                    return null;
                }
                return Push(kind, data, level, message, stack);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private QAEvent Push(string kind, JObject data, string level, string message, string stack)
        {
            var e = new QAEvent
            {
                RunId = _runId,
                Seq = Interlocked.Increment(ref _lastSeq),
                T = _clock.Seconds,
                Ts = Timestamps.Iso(_clock.UtcNow),
                Frame = _state.Frame,
                Kind = kind,
                Scene = _state.Scene,
                Pos = _state.Position,
                Level = level,
                Message = message,
                Stack = string.IsNullOrEmpty(stack) ? null : stack,
                Data = data,
            };
            _queue.Enqueue(e);
            return e;
        }

        /// <summary>A log event; an empty <paramref name="stack"/> is written as null.</summary>
        public QAEvent Log(string level, string message, string stack) =>
            Enqueue(EventKinds.Log, null, level, message ?? string.Empty, stack);

        /// <summary><c>run_start</c>, <c>run_end</c>, <c>scene_loaded</c>, ... with optional details.</summary>
        public QAEvent Marker(string marker, JObject details = null) =>
            Enqueue(EventKinds.Marker, MarkerData(marker, details));

        private static JObject MarkerData(string marker, JObject details)
        {
            var data = new JObject { ["marker"] = marker };
            if (details != null)
            {
                data["details"] = details;
            }
            return data;
        }

        /// <summary>A metric event; pass <c>memMb = null</c> when the platform reports nothing.</summary>
        public QAEvent Metric(double fps, double frameMs, double frameMsP95, double gcMb, double? memMb)
        {
            var data = new JObject
            {
                ["fps"] = Math.Round(fps, 1),
                ["frame_ms"] = Math.Round(frameMs, 1),
                ["frame_ms_p95"] = Math.Round(frameMsP95, 1),
            };
            if (memMb.HasValue)
            {
                data["mem_mb"] = Math.Round(memMb.Value, 1);
            }
            data["gc_mb"] = Math.Round(gcMb, 1);
            return Enqueue(EventKinds.Metric, data);
        }

        // ---- consumer (main thread only) ----------------------------------------------------------

        /// <summary>Write everything queued so far, sorted by <c>seq</c>. Returns the number written.</summary>
        public int Drain()
        {
            if (_closed)
            {
                return 0;
            }
            _batch.Clear();
            while (_queue.TryDequeue(out var e))
            {
                _batch.Add(e);
            }
            if (_batch.Count == 0)
            {
                return 0;
            }
            _batch.Sort(BySeq);
            foreach (var e in _batch)
            {
                _out.Write(e.ToJsonLine());
                _out.Write('\n');
            }
            _out.Flush();
            Interlocked.Add(ref _written, _batch.Count);
            return _batch.Count;
        }

        /// <summary>
        /// End the run (main thread): stop accepting events, let producers already in flight enqueue,
        /// write <paramref name="marker"/> as the last event, drain and close the file. Returns the
        /// marker event, or null if the writer was already closed.
        /// </summary>
        public QAEvent Close(string marker, JObject details = null)
        {
            if (!Seal())
            {
                return null;
            }
            var last = Push(EventKinds.Marker, MarkerData(marker, details), null, null, null);
            Finish();
            return last;
        }

        /// <summary>Like <see cref="Close"/> without a closing marker. Later events are dropped.</summary>
        public void Dispose()
        {
            if (Seal())
            {
                Finish();
            }
        }

        private bool Seal()
        {
            if (Interlocked.Exchange(ref _sealed, 1) == 1)
            {
                return false;
            }
            SpinWait.SpinUntil(() => Volatile.Read(ref _inFlight) == 0, CloseWait);
            return true;
        }

        private void Finish()
        {
            try
            {
                Drain();
            }
            finally
            {
                // Release the file even if the last write fails (disk full): the writer is sealed, so
                // nothing else would close it, and Windows would keep events.jsonl locked.
                _closed = true;
                _out.Dispose();
            }
        }
    }
}
