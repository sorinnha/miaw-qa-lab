using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiawWorks.QALab
{
    /// <summary>
    /// The one "QALab" object (DontDestroyOnLoad). Every frame, in this order: refresh the main-thread
    /// cache, step the bot, run the detectors, plan a screenshot, feed the metrics sampler; every 0.5 s
    /// write queued events; at the end close the run (run_end, run.json, results.xml, labels.json).
    /// All file writes happen here, on the main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QALabHost : MonoBehaviour
    {
        private const float DrainInterval = 0.5f;
        private const int MaxLoggedErrors = 3;

        private QALabOptions _options;
        private string _runId;
        private readonly MainThreadCache _cache = new MainThreadCache();
        private readonly StopwatchClock _clock = new StopwatchClock();
        private readonly List<string> _internalErrors = new List<string>();
        private readonly float[] _playerPos = new float[3];
        private EventWriter _writer;
        private RunContext _run;
        private LogCapture _logs;
        private MetricsSampler _metrics;
        private DetectorHub _hub;
        private PerfSpikeDetector _perf;
        private ShotPlanner _shots;
        private ScreenshotService _screenshots;
        private BotRunner _bot;
        private bool _botPending;
        private float _killPlaneY = float.NegativeInfinity;
        private bool _killPlaneMeasured;
        private bool _capturedLastFrame;
        private bool _warnedNoCamera;
        private long _lastExceptions;
        private float _sinceDrain;
        private bool _recording;

        /// <summary>The run folder (<c>&lt;out&gt;/&lt;run_id&gt;</c>) this host writes to.</summary>
        public string RunDir { get; private set; }

        /// <summary>True from <see cref="Begin"/> until the run ends.</summary>
        public bool IsRecording => _recording;

        /// <summary>
        /// Start recording into <paramref name="runDir"/>. Called by <see cref="QALab"/> right after
        /// AddComponent, before the first scene loads (and by PlayMode tests).
        /// </summary>
        internal void Begin(QALabOptions options, string runId, string runDir)
        {
            _options = options;
            _runId = runId;
            RunDir = runDir;

            var scene = SceneManager.GetActiveScene().name;
            _cache.SetScene(scene);
            _cache.Capture(Time.frameCount, QALab.Player);

            _writer = EventWriter.ToFile(runId, _clock, _cache, Path.Combine(runDir, "events.jsonl"));
            _run = new RunContext(options, runId, runDir, _clock, SeedsEnabled(options));
            _run.AddScene(scene);
            _run.WriteStart();

            LabelRecorder.Begin(options.Benchmark, _clock, _cache);
            // run_start first, so it gets seq 0 before any log from another thread can.
            _writer.Marker("run_start", new JObject { ["adapter"] = options.Adapter, ["seed"] = options.Seed });
            _logs = new LogCapture(_writer, options.MinLevel);
            _logs.Start();
            _metrics = new MetricsSampler(_writer);

            _shots = new ShotPlanner(options.ShotEveryS);
            _screenshots = new ScreenshotService(runDir, _writer, _clock,
                onCaptured: () =>
                {
                    _capturedLastFrame = true;
                    _metrics.ExcludeCurrentFrame();
                },
                onError: InternalError);
            _hub = new DetectorHub(_writer, _clock, _cache, new RateLimiter(),
                () => ScreenshotService.CanCapture ? _shots.RequestDetectorShot() : null, Warn);
            _perf = new PerfSpikeDetector();
            _perf.Suppress();   // the first frames of a run are loading frames
            _hub.Add(new StuckDetector());
            _hub.Add(new FallDetector(() => _killPlaneY, RespawnAfterFall));
            _hub.Add(_perf);
            _hub.Add(new ExceptionBurstDetector());
            CreateBot(options);

            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Application.quitting += OnQuitting;
            _recording = true;
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString("QALab.LastRunDir", runDir);
#endif
            // Invariant culture: scripts/run_playtest.ps1 parses this line ("for 120 s", never "for 120,5 s").
            Debug.Log($"[QALab] recording run {runId} to {runDir} for {options.DurationS.ToString("0.#", CultureInfo.InvariantCulture)} s, adapter '{options.Adapter}'");
        }

        /// <summary>Report through the hub (game detectors use <see cref="QALab.ReportDetector"/>).</summary>
        internal QAEvent ReportDetector(string detector, string severity, JObject details, float[] pos) =>
            _recording ? _hub.Report(detector, severity, details, pos) : null;

        /// <summary>Plan a manual screenshot for the end of this frame; returns its path.</summary>
        internal string RequestScreenshot() => _recording && ScreenshotService.CanCapture ? _shots.RequestManualShot() : null;

        private void CreateBot(QALabOptions options)
        {
            if (options.Adapter == "manual") return;
            var adapter = BotAdapterRegistry.Create(options.Adapter);
            if (adapter == null)
            {
                InternalError($"unknown bot adapter '{options.Adapter}' (registered: {string.Join(", ", BotAdapterRegistry.Names)}); this run has no bot");
                return;
            }
            _bot = new BotRunner(adapter, options.Seed, _writer, () => Math.Max(0f, options.DurationS - (float)_clock.Seconds), Warn);
            _botPending = true;   // starts on the first frame of the requested scene
        }

        private void Start()
        {
            // -qalabScene: load the requested scene once the first one is up.
            var wanted = _options?.Scene;
            if (!string.IsNullOrEmpty(wanted) && SceneManager.GetActiveScene().name != wanted)
            {
                SceneManager.LoadScene(wanted);
            }
        }

        private void Update()
        {
            if (!_recording) return;
            var dt = Time.unscaledDeltaTime;
            _cache.Capture(Time.frameCount, QALab.Player);
            var captureFrame = _capturedLastFrame;
            _capturedLastFrame = false;
            try
            {
                StepBotAndDetectors(dt, captureFrame);
            }
            catch (Exception exc)
            {
                InternalError($"host update: {exc.GetType().Name}: {exc.Message}");
            }
            _metrics.Tick(dt);
            _sinceDrain += dt;
            if (_sinceDrain >= DrainInterval)
            {
                _sinceDrain = 0f;
                _writer.Drain();
            }
            if (_clock.Seconds >= _options.DurationS)
            {
                EndRun(ExitReasons.DurationElapsed);
            }
        }

        private void StepBotAndDetectors(float dt, bool captureFrame)
        {
            if (!_killPlaneMeasured) MeasureKillPlane();
            if (_botPending && SceneReady())
            {
                _botPending = false;
                _bot.Begin();
            }
            _bot?.Tick(dt);
            if (_bot?.Error != null && !_internalErrors.Contains(_bot.Error)) InternalError(_bot.Error);

            var exceptions = _logs.ExceptionCount;
            var newExceptions = (int)Math.Min(int.MaxValue, exceptions - _lastExceptions);
            _lastExceptions = exceptions;
            var frame = new DetectorFrame(_clock.Seconds, Time.frameCount, dt * 1000f, captureFrame, PlayerPosition(),
                _bot != null && _bot.IsMoving, newExceptions);
            _hub.Tick(in frame);

            if (ManualShotKey.WasPressed()) _shots.RequestManualShot();
            var shot = _shots.TakeDue(_clock.Seconds);
            if (shot == null) return;
            if (!ScreenshotService.CanCapture)
            {
                if (!_warnedNoCamera) Warn("batch mode without a MainCamera-tagged camera: no screenshots this run");
                _warnedNoCamera = true;
            }
            else if (Application.isBatchMode)
            {
                _screenshots.Capture(shot);   // no end-of-frame rendering in batch mode; a camera renders any time
            }
            else
            {
                StartCoroutine(_screenshots.CaptureAtEndOfFrame(shot));
            }
        }

        /// <summary>
        /// Close the run: stop the bot, write labels.json and results.xml, then the run_end marker and
        /// run.json with the final exit code. Each file is written on its own, so one failure doesn't
        /// lose the others; a failure is a QA Lab internal error (exit code 2), and because run_end and
        /// run.json come last they always carry the code the process exits with.
        /// </summary>
        internal void EndRun(string exitReason)
        {
            if (!_recording) return;
            _recording = false;
            Try("bot", () => _bot?.Stop(exitReason));
            if (_bot?.Error != null && !_internalErrors.Contains(_bot.Error)) InternalError(_bot.Error);
            Try("labels.json", () =>
            {
                var unknown = LabelRecorder.End(Path.Combine(RunDir, "labels.json"), _runId);
                if (unknown.Count > 0)
                {
                    // Count only: seed ids must never appear in log text (player.log sits in the run folder).
                    Debug.LogWarning($"[QALab] {unknown.Count} triggered seed(s) have no catalog entry and are missing from labels.json");
                }
            });
            Try("results.xml", () => JUnitWriter.WriteTo(Path.Combine(RunDir, "results.xml"), Results(ExitCode())));
            var exitCode = ExitCode();
            Try("log capture", () => _logs.Stop());   // no new log callbacks; Close waits for any still in flight
            Try("events.jsonl", () => _writer.Close("run_end", new JObject { ["exit_reason"] = exitReason, ["exit_code"] = exitCode }));
            Try("run.json", () => _run.WriteEnd(exitReason, exitCode));
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            Application.quitting -= OnQuitting;
            exitCode = ExitCode();   // a failed run.json write still makes the process report it
            Debug.Log($"[QALab] run {_runId} ended ({exitReason}, exit code {exitCode}): {RunDir}");
            if (_options.QuitOnEnd && exitReason == ExitReasons.DurationElapsed)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit(exitCode);
#endif
            }
        }

        private int ExitCode() => ExitCodes.For(_hub.FatalFired, _hub.Failed.Count > 0 || _internalErrors.Count > 0);

        private void Try(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception exc)
            {
                InternalError($"closing the run ({what}): {exc.GetType().Name}: {exc.Message}");
            }
        }

        private RunResults Results(int exitCode)
        {
            var results = new RunResults
            {
                RunId = _runId,
                Seed = _options.Seed,
                Adapter = _options.Adapter,
                DurationS = _clock.Seconds,
                StartedAtUtc = _clock.UtcNow.AddSeconds(-_clock.Seconds),
                ExitCode = exitCode,
                Exceptions = _logs.ExceptionCount,
            };
            results.AddDetectorsFrom(_hub);
            results.InternalErrors.AddRange(_internalErrors);
            return results;
        }

        /// <summary>The fall detector's follow-up: put the player back and log it as a bot step.</summary>
        private void RespawnAfterFall()
        {
            var mover = _bot?.Mover ?? QALab.Mover;
            if (mover == null) return;
            mover.Respawn();
            _bot?.LogRespawn(DetectorNames.FellOutOfWorld);
        }

        private bool SceneReady()
        {
            var wanted = _options.Scene;
            return string.IsNullOrEmpty(wanted) || SceneManager.GetActiveScene().name == wanted;
        }

        // Filled in place every frame (no allocation); detectors read it during Tick only.
        private float[] PlayerPosition()
        {
            var player = QALab.Player;
            if (player == null) return null;
            var p = player.position;
            _playerPos[0] = p.x;
            _playerPos[1] = p.y;
            _playerPos[2] = p.z;
            return _playerPos;
        }

        private void MeasureKillPlane()
        {
            _killPlaneY = KillPlane.Measure();
            _killPlaneMeasured = true;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _run.AddScene(scene.name);
            _cache.SetScene(SceneManager.GetActiveScene().name);
            _writer.Marker("scene_loaded", new JObject { ["scene"] = scene.name });
            _killPlaneMeasured = false;   // measured on the next frame, when the scene's objects exist
            _perf.Suppress();
        }

        private void OnActiveSceneChanged(Scene previous, Scene next) => _cache.SetScene(next.name);

        private void OnQuitting() => EndRun(ExitReasons.UserQuit);

        // Leaving Play Mode or quitting destroys this object: a clean end, if the run is still open.
        private void OnDestroy() => EndRun(ExitReasons.UserQuit);

        private void InternalError(string message)
        {
            _internalErrors.Add(message);
            if (_internalErrors.Count <= MaxLoggedErrors)
            {
                Debug.LogWarning("[QALab] internal error: " + message);
            }
        }

        private static void Warn(string message) => Debug.LogWarning("[QALab] " + message);

        /// <summary>run.json → seeds_enabled (rule in <see cref="SeedSelection.EnabledIds"/>).</summary>
        private static IEnumerable<string> SeedsEnabled(QALabOptions options)
        {
            var catalogIds = new List<string>();
            foreach (var entry in LabelRecorder.Catalog)
            {
                catalogIds.Add(entry.BugId);
            }
            return options.Seeds.EnabledIds(catalogIds);
        }
    }
}
