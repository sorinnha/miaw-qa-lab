using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Runs one <see cref="IBotAdapter"/> for a run (spec 01, "Bot framework"): <c>Begin</c> once,
    /// <c>Step</c> every <see cref="DecisionIntervalS"/> until it returns Done or the run ends, then
    /// <c>End</c>. The host owns it and calls <see cref="Tick"/> from its Update, so the bot, the
    /// detectors and the screenshots run in a fixed order every frame.
    /// The adapter sees the game's mover through a wrapper that remembers the last move target and
    /// re-issues it every frame (the <see cref="IBotMover"/> contract), so adapters only decide every
    /// 0.25 s, and the stuck detector knows when the bot is trying to move.
    /// An adapter that throws stops the bot for the rest of the run (a QA Lab internal error); the run
    /// itself goes on recording.
    /// </summary>
    public sealed class BotRunner
    {
        public const float DefaultDecisionIntervalS = 0.25f;

        private readonly IBotAdapter _adapter;
        private readonly EventWriter _writer;
        private readonly TrackingMover _mover;
        private readonly Action<string> _warn;
        private float _sinceStep;
        private bool _running;
        private bool _done;

        /// <param name="timeLeft">Seconds until the run ends.</param>
        /// <param name="warn">Where QA Lab's own warnings go (a <c>[QALab]</c> logger).</param>
        public BotRunner(IBotAdapter adapter, int seed, EventWriter writer, Func<float> timeLeft, Action<string> warn,
            float decisionIntervalS = DefaultDecisionIntervalS)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _warn = warn ?? (_ => { });
            if (!(decisionIntervalS > 0f)) throw new ArgumentOutOfRangeException(nameof(decisionIntervalS));
            DecisionIntervalS = decisionIntervalS;
            _mover = new TrackingMover(() => QALab.Mover);
            Context = new BotContext(adapter.Name, new SeededRandom(seed), () => QALab.Player,
                () => QALab.Mover == null ? null : _mover, timeLeft,
                data => _writer.Enqueue(EventKinds.Action, data));
        }

        public float DecisionIntervalS { get; }

        public BotContext Context { get; }

        public string AdapterName => _adapter.Name;

        /// <summary>True while a move target is set: the bot is asking the player to walk.</summary>
        public bool IsMoving => _running && _mover.HasTarget;

        /// <summary>Set when the adapter threw; the bot is stopped.</summary>
        public string Error { get; private set; }

        /// <summary>The mover the bot drives (the game's, wrapped), or null when no player is registered.</summary>
        public IBotMover Mover => QALab.Mover == null ? null : _mover;

        /// <summary>Write <c>bot_started</c> and call the adapter's Begin.</summary>
        public void Begin()
        {
            if (_running || Error != null) return;
            _writer.Marker("bot_started", new JObject { ["adapter"] = _adapter.Name, ["seed"] = Context.Random.Seed });
            _running = true;
            Guard("Begin", () => _adapter.Begin(Context));
        }

        /// <summary>Called every frame with the unscaled frame time.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (!_running) return;
            _mover.Reissue();
            if (_done) return;
            _sinceStep += unscaledDeltaTime;
            if (_sinceStep < DecisionIntervalS) return;
            _sinceStep = 0f;
            Guard("Step", () =>
            {
                if (_adapter.Step(Context) == BotStepResult.Done)
                {
                    _done = true;
                    _mover.Stop();
                }
            });
        }

        /// <summary>
        /// Record a respawn the run caused (the fall detector), as a repro step of this adapter, so
        /// "steps to reproduce" show it. Called after <c>Respawn()</c>, so the target is where the player
        /// is now.
        /// </summary>
        public void LogRespawn(string reason)
        {
            if (!_running) return;
            var player = QALab.Player;
            Context.LogAction("respawn", player != null ? player.position : (Vector3?)null, null,
                new JObject { ["reason"] = reason });
        }

        /// <summary>Call the adapter's End, stop the player and write <c>bot_stopped</c>.</summary>
        public void Stop(string reason)
        {
            if (!_running) return;
            Guard("End", () => _adapter.End(Context));
            if (!_running) return;   // End threw: Guard already stopped the bot and wrote bot_stopped
            _running = false;
            _mover.Stop();
            _writer.Marker("bot_stopped", new JObject { ["reason"] = reason, ["steps"] = Context.Step });
        }

        private void Guard(string phase, Action call)
        {
            try
            {
                call();
            }
            catch (Exception exc)
            {
                Error = $"bot adapter '{_adapter.Name}' {phase}: {exc.GetType().Name}: {exc.Message}";
                _warn(Error + " (the bot is stopped; the run keeps recording)");
                _running = false;
                _mover.Stop();
                _writer.Marker("bot_stopped", new JObject { ["reason"] = "error", ["steps"] = Context.Step });
            }
        }

        /// <summary>
        /// The game's mover as the adapter sees it: remembers the last target and re-issues it every frame
        /// until Stop, Respawn or a new target.
        /// </summary>
        private sealed class TrackingMover : IBotMover
        {
            private readonly Func<IBotMover> _inner;
            private Vector3 _target;

            public TrackingMover(Func<IBotMover> inner) => _inner = inner;

            public bool HasTarget { get; private set; }

            public void Reissue()
            {
                if (HasTarget) _inner()?.MoveTowards(_target);
            }

            public void MoveTowards(Vector3 worldTarget)
            {
                _target = worldTarget;
                HasTarget = true;
                _inner()?.MoveTowards(worldTarget);
            }

            public void Stop()
            {
                var wasMoving = HasTarget;
                HasTarget = false;
                if (wasMoving) _inner()?.Stop();
            }

            public bool TryInteract(out string objectName)
            {
                objectName = null;
                var inner = _inner();
                return inner != null && inner.TryInteract(out objectName);
            }

            public void Respawn()
            {
                HasTarget = false;
                _inner()?.Respawn();
            }
        }
    }
}
