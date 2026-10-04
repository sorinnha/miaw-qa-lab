using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// What an <see cref="IBotAdapter"/> gets each call (spec 01): the seeded RNG, the registered player
    /// and its <see cref="IBotMover"/>, the time left, and <see cref="LogAction"/> for repro steps.
    /// The bot runner (M4) builds one per run and passes a writer for action events.
    /// </summary>
    public sealed class BotContext
    {
        private readonly Func<float> _timeLeft;
        private readonly Action<JObject> _writeAction;

        /// <param name="writeAction">Writes the data of one <c>action</c> event (the runner passes
        /// the run's event writer).</param>
        public BotContext(string adapterName, SeededRandom random, Transform player, IBotMover mover,
            Func<float> timeLeft, Action<JObject> writeAction)
        {
            AdapterName = adapterName;
            Random = random ?? throw new ArgumentNullException(nameof(random));
            Player = player;
            Mover = mover;
            _timeLeft = timeLeft ?? throw new ArgumentNullException(nameof(timeLeft));
            _writeAction = writeAction ?? throw new ArgumentNullException(nameof(writeAction));
        }

        public string AdapterName { get; }

        /// <summary>The only RNG bot code may use (never <c>UnityEngine.Random</c>).</summary>
        public SeededRandom Random { get; }

        /// <summary>The player registered with <see cref="QALab.RegisterPlayer"/>; null if none.</summary>
        public Transform Player { get; }

        public IBotMover Mover { get; }

        /// <summary>Seconds until the run ends.</summary>
        public float TimeLeft => _timeLeft();

        /// <summary>Actions logged so far. The first action is step 1.</summary>
        public int Step { get; private set; }

        /// <summary>
        /// Record one repro step as an <c>action</c> event, e.g. <c>LogAction("move_to", target)</c> or
        /// <c>LogAction("end_turn")</c>. Log what the bot did, in words a tester would use: triage
        /// turns these into "steps to reproduce".
        /// </summary>
        public void LogAction(string action, Vector3? target = null, string uiPath = null, JObject args = null)
        {
            Step++;
            var position = target.HasValue ? new[] { target.Value.x, target.Value.y, target.Value.z } : null;
            _writeAction(BotActionData.Build(action, Step, AdapterName, position, uiPath, args));
        }
    }
}
