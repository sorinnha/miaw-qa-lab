using System;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Turns Unity log messages into <c>log</c> events. It listens to
    /// <c>Application.logMessageReceivedThreaded</c>, which fires on whatever thread logged, so the
    /// handler only reads its own fields, enqueues and returns: no Unity API calls here.
    /// </summary>
    public sealed class LogCapture
    {
        private readonly EventWriter _writer;
        private readonly string _minLevel;
        private bool _listening;

        public LogCapture(EventWriter writer, string minLevel)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _minLevel = minLevel;
        }

        public void Start()
        {
            if (_listening) return;
            Application.logMessageReceivedThreaded += OnLog;
            _listening = true;
        }

        public void Stop()
        {
            if (!_listening) return;
            Application.logMessageReceivedThreaded -= OnLog;
            _listening = false;
        }

        /// <summary>Unity's LogType → qalab.event/1 level (spec 00).</summary>
        public static string LevelOf(LogType type)
        {
            switch (type)
            {
                case LogType.Warning: return LogLevels.Warning;
                case LogType.Error: return LogLevels.Error;
                case LogType.Exception: return LogLevels.Exception;
                case LogType.Assert: return LogLevels.Assert;
                default: return LogLevels.Info;
            }
        }

        /// <summary>
        /// Development players: no stack for plain logs (cheap), script-only stacks for the rest so
        /// triage gets frames without the engine noise. The editor keeps its own settings.
        /// </summary>
        public static void ConfigureStackTraces()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            Application.SetStackTraceLogType(LogType.Warning, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            Application.SetStackTraceLogType(LogType.Assert, StackTraceLogType.ScriptOnly);
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            try
            {
                var level = LevelOf(type);
                if (!LogLevels.Passes(level, _minLevel) || LogLevels.IsOwnMessage(condition))
                {
                    return;
                }
                // Unity ends stack traces with "\n"; the contract has frames separated by "\n" only.
                _writer.Log(level, condition, stackTrace?.TrimEnd('\n', '\r'));
            }
            catch (Exception)
            {
                // Never throw back into Unity's logger (it would recurse or crash the callback).
            }
        }
    }
}
