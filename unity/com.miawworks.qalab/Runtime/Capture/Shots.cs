// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>Screenshot names, sizes and event data (spec 00 run folder, spec 01 "Screenshots").</summary>
    public static class Shots
    {
        public const string Folder = "shots";

        /// <summary>Long side cap in pixels: enough for vision models, small enough to store many.</summary>
        public const int MaxLongSide = 1280;

        public const string Periodic = "periodic";
        public const string Detector = "detector";
        public const string Manual = "manual";

        /// <summary>Full fidelity: the frame as the player saw it, overlay UI included.</summary>
        public const string ScreenCapture = "screen_capture";

        /// <summary>Batch-mode fallback: <c>Camera.main</c> rendered into a texture. Overlay UI is missing.</summary>
        public const string CameraRender = "camera_render";

        /// <summary>Run-relative path of shot <paramref name="number"/> (1-based): <c>shots/000001.png</c>.</summary>
        public static string RelativePath(int number)
        {
            if (number < 1) throw new ArgumentOutOfRangeException(nameof(number), "shots are numbered from 1");
            return Folder + "/" + number.ToString("000000", CultureInfo.InvariantCulture) + ".png";
        }

        /// <summary>
        /// <paramref name="width"/> × <paramref name="height"/> scaled down (never up) so the long side is at
        /// most <paramref name="maxLongSide"/>, keeping the aspect ratio. Each side is at least 1 px.
        /// </summary>
        public static (int Width, int Height) Fit(int width, int height, int maxLongSide = MaxLongSide)
        {
            if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width), "size must be positive");
            if (maxLongSide < 1) throw new ArgumentOutOfRangeException(nameof(maxLongSide));
            var longSide = Math.Max(width, height);
            if (longSide <= maxLongSide) return (width, height);
            var scale = (double)maxLongSide / longSide;
            return (Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
        }

        /// <summary>The <c>data</c> of a <c>screenshot</c> event (schemas/event.schema.json, screenshotData).</summary>
        public static JObject Data(string path, string reason, int width, int height, string method)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is required", nameof(path));
            if (reason != Periodic && reason != Detector && reason != Manual)
            {
                throw new ArgumentException($"bad reason '{reason}'", nameof(reason));
            }
            if (method != ScreenCapture && method != CameraRender)
            {
                throw new ArgumentException($"bad method '{method}'", nameof(method));
            }
            return new JObject
            {
                ["path"] = path,
                ["reason"] = reason,
                ["w"] = width,
                ["h"] = height,
                ["method"] = method,
            };
        }
    }

    /// <summary>A screenshot planned for the end of the current frame.</summary>
    public sealed class PlannedShot
    {
        public PlannedShot(int number, string reason)
        {
            Number = number;
            Path = Shots.RelativePath(number);
            Reason = reason;
        }

        public int Number { get; }

        /// <summary>Run-relative path, e.g. <c>shots/000003.png</c>.</summary>
        public string Path { get; }

        /// <summary><c>periodic</c>, <c>detector</c> or <c>manual</c>.</summary>
        public string Reason { get; internal set; }
    }

    /// <summary>
    /// Decides when screenshots are taken and numbers them. At most one shot per frame: a detector that
    /// fires while a shot is already planned for this frame shares it (both events point to the same
    /// file), and a periodic shot that falls due in the same frame is folded into it. Detector shots
    /// get their path at once, so the detector event can point to the file before it exists.
    /// Main thread only.
    /// </summary>
    public sealed class ShotPlanner
    {
        private readonly double _everyS;
        private double _nextPeriodic;
        private int _lastNumber;
        private PlannedShot _pending;

        /// <param name="everyS">Seconds between periodic shots; 0 turns them off (-qalabShotEvery).</param>
        public ShotPlanner(double everyS)
        {
            if (everyS < 0.0 || double.IsNaN(everyS)) throw new ArgumentOutOfRangeException(nameof(everyS));
            _everyS = everyS;
            _nextPeriodic = everyS;
        }

        /// <summary>Shots numbered so far (the last file is <c>shots/{Planned:000000}.png</c>).</summary>
        public int Planned => _lastNumber;

        /// <summary>Path of the shot a detector event should point to (planned now if needed).</summary>
        public string RequestDetectorShot() => Request(Shots.Detector).Path;

        /// <summary>A shot the tester asked for (F12).</summary>
        public string RequestManualShot() => Request(Shots.Manual).Path;

        /// <summary>
        /// Called once per frame (end of the host's Update) with the run time. Returns the shot to capture at
        /// the end of this frame, or null. Clears the plan.
        /// </summary>
        public PlannedShot TakeDue(double t)
        {
            var periodicDue = _everyS > 0.0 && t >= _nextPeriodic;
            if (periodicDue)
            {
                // Schedule from now, not from the missed time: a long hitch never causes a burst of shots.
                while (_nextPeriodic <= t) _nextPeriodic += _everyS;
                if (_pending == null) Request(Shots.Periodic);
            }
            var due = _pending;
            _pending = null;
            return due;
        }

        private PlannedShot Request(string reason)
        {
            if (_pending == null)
            {
                _pending = new PlannedShot(++_lastNumber, reason);
            }
            else if (reason == Shots.Detector)
            {
                _pending.Reason = Shots.Detector;   // detector beats manual: the event points to this file
            }
            return _pending;
        }
    }
}
