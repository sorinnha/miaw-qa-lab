// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// One line of <c>events.jsonl</c>; fields map 1:1 to <c>qalab.event/1</c> (schemas/event.schema.json).
    /// <c>scene</c> and <c>pos</c> are always written (null allowed); <c>stack</c> only on log events;
    /// the other optional fields are left out when null.
    /// </summary>
    public sealed class QAEvent
    {
        public const string SchemaId = "qalab.event/1";

        [JsonProperty("schema", Order = 0)] public string Schema = SchemaId;
        [JsonProperty("run_id", Order = 1)] public string RunId;
        [JsonProperty("seq", Order = 2)] public long Seq;
        [JsonProperty("t", Order = 3)] public double T;
        [JsonProperty("ts", Order = 4, NullValueHandling = NullValueHandling.Ignore)] public string Ts;
        [JsonProperty("frame", Order = 5, NullValueHandling = NullValueHandling.Ignore)] public long? Frame;
        [JsonProperty("kind", Order = 6)] public string Kind;
        [JsonProperty("scene", Order = 7, NullValueHandling = NullValueHandling.Include)] public string Scene;
        [JsonProperty("pos", Order = 8, NullValueHandling = NullValueHandling.Include)] public float[] Pos;
        [JsonProperty("level", Order = 9, NullValueHandling = NullValueHandling.Ignore)] public string Level;
        [JsonProperty("message", Order = 10, NullValueHandling = NullValueHandling.Ignore)] public string Message;
        [JsonProperty("stack", Order = 11, NullValueHandling = NullValueHandling.Include)] public string Stack;
        [JsonProperty("data", Order = 12, NullValueHandling = NullValueHandling.Ignore)] public JObject Data;

        /// <summary>Newtonsoft calls this to decide whether to write <c>stack</c>: log events only.</summary>
        public bool ShouldSerializeStack() => Kind == EventKinds.Log;

        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            // Keep "2026-10-05T10:30:08.210Z" as a string instead of turning it into a DateTime.
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };

        /// <summary>One compact JSON line, without the trailing newline.</summary>
        public string ToJsonLine() => JsonConvert.SerializeObject(this, Settings);

        public static QAEvent FromJson(string line) => JsonConvert.DeserializeObject<QAEvent>(line, Settings);
    }

    /// <summary>The <c>kind</c> values of qalab.event/1.</summary>
    public static class EventKinds
    {
        public const string Log = "log";
        public const string Action = "action";
        public const string Detector = "detector";
        public const string Metric = "metric";
        public const string Screenshot = "screenshot";
        public const string Marker = "marker";
    }
}
