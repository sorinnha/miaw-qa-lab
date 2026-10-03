// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using Newtonsoft.Json;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Ground truth for one seeded bug: one item of <c>labels.json → seeded_bugs</c> (qalab.labels/1)
    /// without its triggers. Games describe their seeds in code (see the sandbox's SandboxSeedCatalog).
    /// </summary>
    public sealed class SeedCatalogEntry
    {
        [JsonProperty("bug_id", Order = 0)] public string BugId;
        /// <summary><c>log</c>, <c>detector</c> or <c>visual</c>.</summary>
        [JsonProperty("type", Order = 1)] public string Type;
        [JsonProperty("title", Order = 2, NullValueHandling = NullValueHandling.Ignore)] public string Title;
        /// <summary>Exact H2 heading in docs/sandbox_design.md (retrieval ground truth).</summary>
        [JsonProperty("feature", Order = 3)] public string Feature;
        /// <summary><c>S1</c>..<c>S4</c>.</summary>
        [JsonProperty("expected_severity", Order = 4)] public string ExpectedSeverity;
        [JsonProperty("match", Order = 5)] public MatchRule Match = new MatchRule();
    }

    /// <summary>How <c>qalab eval</c> maps events to a seeded bug. Every field that is set must match.</summary>
    public sealed class MatchRule
    {
        [JsonProperty("stack_contains", NullValueHandling = NullValueHandling.Ignore)] public string StackContains;
        [JsonProperty("message_regex", NullValueHandling = NullValueHandling.Ignore)] public string MessageRegex;
        [JsonProperty("detector", NullValueHandling = NullValueHandling.Ignore)] public string Detector;
        [JsonProperty("near", NullValueHandling = NullValueHandling.Ignore)] public float[] Near;
        [JsonProperty("radius", NullValueHandling = NullValueHandling.Ignore)] public float? Radius;
        [JsonProperty("visual_label", NullValueHandling = NullValueHandling.Ignore)] public string VisualLabel;

        /// <summary>True when at least one rule is set (the schema requires one). Not serialized.</summary>
        [JsonIgnore]
        public bool HasAnyRule =>
            StackContains != null || MessageRegex != null || Detector != null
            || Near != null || Radius != null || VisualLabel != null;
    }
}
