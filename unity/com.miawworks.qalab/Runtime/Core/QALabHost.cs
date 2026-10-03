using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiawWorks.QALab
{
    /// <summary>
    /// The one "QALab" object (DontDestroyOnLoad). Every frame it refreshes the main-thread cache and
    /// feeds the metrics sampler; every 0.5 s it writes queued events; at the end it closes the run.
    /// All file writes happen here, on the main thread.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class QALabHost : MonoBehaviour
    {
        private const float DrainInterval = 0.5f;

        private QALabOptions _options;
        private string _runId;
        private readonly MainThreadCache _cache = new MainThreadCache();
        private readonly StopwatchClock _clock = new StopwatchClock();
        private EventWriter _writer;
        private RunContext _run;
        private LogCapture _logs;
        private MetricsSampler _metrics;
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
            SceneManager.sceneLoaded += OnSceneLoaded;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            Application.quitting += OnQuitting;
            _recording = true;
#if UNITY_EDITOR
            UnityEditor.EditorPrefs.SetString("QALab.LastRunDir", runDir);
#endif
            Debug.Log($"[QALab] recording run {runId} to {runDir} for {options.DurationS:0.#} s");
            if (options.Adapter != "manual")
            {
                Debug.Log($"[QALab] adapter '{options.Adapter}' arrives with the bot in M4; this run has no bot.");
            }
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
            _cache.Capture(Time.frameCount, QALab.Player);
            _metrics.Tick(Time.unscaledDeltaTime);
            _sinceDrain += Time.unscaledDeltaTime;
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

        /// <summary>Close the run: run_end marker, final drain, run.json with ended_at, labels.json.</summary>
        internal void EndRun(string exitReason)
        {
            if (!_recording) return;
            _recording = false;
            const int exitCode = 0;   // M4: 1 when a blocker/critical detector fired, 2 on internal error
            try
            {
                _writer.Marker("run_end", new JObject { ["exit_reason"] = exitReason });
                _logs.Stop();
                _writer.Dispose();
                _run.WriteEnd(exitReason, exitCode);
                var unknown = LabelRecorder.End(Path.Combine(RunDir, "labels.json"), _runId);
                if (unknown.Count > 0)
                {
                    // Count only: seed ids must never appear in log text (player.log sits in the run folder).
                    Debug.LogWarning($"[QALab] {unknown.Count} triggered seed(s) have no catalog entry and are missing from labels.json");
                }
                Debug.Log($"[QALab] run {_runId} ended ({exitReason}): {RunDir}");
            }
            catch (Exception exc)
            {
                Debug.LogError("[QALab] could not close the run: " + exc.Message);
            }
            finally
            {
                SceneManager.sceneLoaded -= OnSceneLoaded;
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
                Application.quitting -= OnQuitting;
            }
            if (_options.QuitOnEnd && exitReason == ExitReasons.DurationElapsed)
            {
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit(exitCode);
#endif
            }
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            _run.AddScene(scene.name);
            _cache.SetScene(SceneManager.GetActiveScene().name);
            _writer.Marker("scene_loaded", new JObject { ["scene"] = scene.name });
        }

        private void OnActiveSceneChanged(Scene previous, Scene next) => _cache.SetScene(next.name);

        private void OnQuitting() => EndRun(ExitReasons.UserQuit);

        // Leaving Play Mode or quitting destroys this object: a clean end, if the run is still open.
        private void OnDestroy() => EndRun(ExitReasons.UserQuit);

        /// <summary>run.json → seeds_enabled: the game's catalog ids that -qalabSeeds turns on.</summary>
        private static IEnumerable<string> SeedsEnabled(QALabOptions options)
        {
            var ids = new List<string>();
            foreach (var entry in LabelRecorder.Catalog)
            {
                if (options.Seeds.IsEnabled(entry.BugId)) ids.Add(entry.BugId);
            }
            if (ids.Count == 0 && !options.Seeds.IsAll)
            {
                ids.AddRange(options.Seeds.Ids);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids;
        }
    }
}
