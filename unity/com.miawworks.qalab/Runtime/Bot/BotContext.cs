using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// What an <see cref="IBotAdapter"/> gets each call (spec 01): the seeded RNG, the registered player
    /// and its <see cref="IBotMover"/>, the time left, and <c>LogAction</c> for repro steps.
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

        /// <summary>
        /// <see cref="LogAction(string, Vector3?, string, JObject)"/> with the details as a plain dictionary,
        /// so adapters don't build JSON: <c>LogAction("order", new Dictionary&lt;string, object&gt; { ["unit"] = "Archer_1" })</c>.
        /// Strings, numbers and bools are written as they are, a <c>Vector3</c> as <c>[x, y, z]</c>, anything
        /// else as its <c>ToString()</c>. (Calling either overload still needs a reference to Newtonsoft.Json,
        /// which game code has by default; see docs/GAME_INTEGRATION.md if your own asmdef overrides references.)
        /// </summary>
        public void LogAction(string action, IDictionary<string, object> args) =>
            LogAction(action, null, null, args == null ? null : ToJson(args));

        private static JObject ToJson(IDictionary<string, object> args)
        {
            var json = new JObject();
            foreach (var pair in args)
            {
                json[pair.Key] = pair.Value switch
                {
                    null => JValue.CreateNull(),
                    string or bool or int or long or float or double or decimal => JToken.FromObject(pair.Value),
                    Vector3 v => new JArray(v.x, v.y, v.z),
                    _ => new JValue(pair.Value.ToString()),
                };
            }
            return json;
        }
    }
}
