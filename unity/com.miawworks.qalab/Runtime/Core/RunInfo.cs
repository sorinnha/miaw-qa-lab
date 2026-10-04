// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace MiawWorks.QALab
{
    /// <summary><c>run.json</c> (<c>qalab.run/1</c>). Written at run start and rewritten at the end.</summary>
    public sealed class RunInfo
    {
        public const string SchemaId = "qalab.run/1";

        [JsonProperty("schema", Order = 0)] public string Schema = SchemaId;
        [JsonProperty("run_id", Order = 1)] public string RunId;
        [JsonProperty("project", Order = 2)] public string Project;
        [JsonProperty("qalab_version", Order = 3, NullValueHandling = NullValueHandling.Ignore)] public string QALabVersion;
        [JsonProperty("build", Order = 4, NullValueHandling = NullValueHandling.Ignore)] public BuildInfo Build;
        [JsonProperty("mode", Order = 5)] public string Mode;
        [JsonProperty("adapter", Order = 6, NullValueHandling = NullValueHandling.Ignore)] public string Adapter;
        [JsonProperty("seed", Order = 7)] public int Seed;
        [JsonProperty("scenes", Order = 8)] public List<string> Scenes = new List<string>();
        [JsonProperty("duration_s", Order = 9, NullValueHandling = NullValueHandling.Ignore)] public double? DurationS;
        [JsonProperty("started_at", Order = 10)] public string StartedAt;
        /// <summary>Null until the run ends cleanly. Missing or null means a possible crash.</summary>
        [JsonProperty("ended_at", Order = 11, NullValueHandling = NullValueHandling.Include)] public string EndedAt;
        [JsonProperty("exit_reason", Order = 12, NullValueHandling = NullValueHandling.Include)] public string ExitReason;
        [JsonProperty("exit_code", Order = 13, NullValueHandling = NullValueHandling.Include)] public int? ExitCode;
        [JsonProperty("machine", Order = 14, NullValueHandling = NullValueHandling.Ignore)] public MachineInfo Machine;
        [JsonProperty("benchmark", Order = 15)] public bool Benchmark;
        [JsonProperty("seeds_enabled", Order = 16)] public List<string> SeedsEnabled = new List<string>();

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented, JsonSettings.Strict);

        public static RunInfo FromJson(string json) => JsonConvert.DeserializeObject<RunInfo>(json, JsonSettings.Strict);

        /// <summary>
        /// Write <c>run.json</c> through a temp file, UTF-8 without BOM, LF. An existing file is swapped in
        /// one call (<see cref="File.Replace(string, string, string)"/>) instead of delete + move, so a
        /// crash between two steps can't leave the folder without a run.json. (If the OS swap itself
        /// fails halfway, the new content is still in <c>run.json.tmp</c>.)
        /// </summary>
        public void WriteTo(string path)
        {
            var temp = path + ".tmp";
            File.WriteAllText(temp, ToJson().Replace("\r\n", "\n") + "\n", new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }

        /// <summary>Mark a clean end: <c>ended_at</c>, duration, exit reason and code.</summary>
        public void MarkEnded(DateTime utcNow, double durationS, string exitReason, int exitCode)
        {
            EndedAt = Timestamps.Iso(utcNow);
            DurationS = Math.Round(durationS, 1);
            ExitReason = exitReason;
            ExitCode = exitCode;
        }
    }

    public sealed class BuildInfo
    {
        [JsonProperty("version", NullValueHandling = NullValueHandling.Ignore)] public string Version;
        [JsonProperty("platform", NullValueHandling = NullValueHandling.Ignore)] public string Platform;
        [JsonProperty("unity", NullValueHandling = NullValueHandling.Ignore)] public string Unity;
        [JsonProperty("git_sha", NullValueHandling = NullValueHandling.Include)] public string GitSha;
        [JsonProperty("development", NullValueHandling = NullValueHandling.Ignore)] public bool? Development;
    }

    public sealed class MachineInfo
    {
        [JsonProperty("os", NullValueHandling = NullValueHandling.Ignore)] public string Os;
        [JsonProperty("cpu", NullValueHandling = NullValueHandling.Ignore)] public string Cpu;
        [JsonProperty("gpu", NullValueHandling = NullValueHandling.Ignore)] public string Gpu;
        [JsonProperty("ram_gb", NullValueHandling = NullValueHandling.Ignore)] public double? RamGb;
    }

    /// <summary>Run ids and exit reasons (spec 00).</summary>
    public static class RunIds
    {
        /// <summary>Windows-safe id <c>&lt;UTC yyyyMMddTHHmmssZ&gt;-s&lt;seed&gt;</c>, e.g. <c>20261005T103000Z-s42</c>.</summary>
        public static string Create(DateTime utc, int seed) =>
            utc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture) + "-s" + seed.ToString(CultureInfo.InvariantCulture);
    }

    public static class ExitReasons
    {
        public const string DurationElapsed = "duration_elapsed";
        public const string FatalDetector = "fatal_detector";
        public const string UserQuit = "user_quit";
        public const string TestFinished = "test_finished";
        public const string Unknown = "unknown";
    }

    internal static class JsonSettings
    {
        /// <summary>Reject unknown fields when reading, mirroring the schemas' additionalProperties: false.</summary>
        public static readonly JsonSerializerSettings Strict = new JsonSerializerSettings
        {
            MissingMemberHandling = MissingMemberHandling.Error,
            DateParseHandling = DateParseHandling.None,
            FloatParseHandling = FloatParseHandling.Double,
        };
    }
}
