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
        private int _disposed;
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

        // ---- producers (any thread) ---------------------------------------------------------------

        /// <summary>Stamp and enqueue an event of any kind. <c>data</c> may be null for log events.</summary>
        public QAEvent Enqueue(string kind, JObject data = null, string level = null, string message = null, string stack = null)
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
            if (Volatile.Read(ref _disposed) == 0)
            {
                _queue.Enqueue(e);
            }
            return e;
        }

        public QAEvent Log(string level, string message, string stack) =>
            Enqueue(EventKinds.Log, null, level, message ?? string.Empty, stack);

        /// <summary><c>run_start</c>, <c>run_end</c>, <c>scene_loaded</c>, ... with optional details.</summary>
        public QAEvent Marker(string marker, JObject details = null)
        {
            var data = new JObject { ["marker"] = marker };
            if (details != null)
            {
                data["details"] = details;
            }
            return Enqueue(EventKinds.Marker, data);
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

        /// <summary>Final drain, then close the file. Events enqueued afterwards are dropped.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }
            Drain();
            _closed = true;
            _out.Dispose();
        }
    }
}
