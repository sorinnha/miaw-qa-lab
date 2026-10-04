// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// The <c>data</c> of an <c>action</c> event (schemas/event.schema.json, <c>actionData</c>). Action
    /// events are the bot's repro steps: triage cites them as "steps to reproduce", so every adapter
    /// writes them the same way through <c>BotContext.LogAction</c>.
    /// </summary>
    public static class BotActionData
    {
        /// <summary>
        /// <c>{action, step, adapter, target?, ui_path?, args?}</c>. <paramref name="target"/> is a world
        /// position (x, y, z), rounded to 1 cm like event positions.
        /// </summary>
        public static JObject Build(string action, int step, string adapter, float[] target = null,
            string uiPath = null, JObject args = null)
        {
            if (string.IsNullOrEmpty(action)) throw new ArgumentException("action is required", nameof(action));
            if (step < 0) throw new ArgumentOutOfRangeException(nameof(step), "step starts at 1");
            if (target != null && target.Length != 3)
            {
                throw new ArgumentException("target is x, y, z", nameof(target));
            }
            var data = new JObject { ["action"] = action, ["step"] = step };
            if (!string.IsNullOrEmpty(adapter)) data["adapter"] = adapter;
            if (target != null) data["target"] = new JArray(Round(target[0]), Round(target[1]), Round(target[2]));
            if (!string.IsNullOrEmpty(uiPath)) data["ui_path"] = uiPath;
            if (args != null) data["args"] = args;
            return data;
        }

        private static float Round(float v) => (float)Math.Round(v, 2);
    }
}
