// Engine-free: compiled by Unity (Editor assembly) and by tools/cs-check (.NET).
// No UnityEngine/UnityEditor here; ProjectScanner.cs feeds it from the editor.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab.Editor
{
    /// <summary>The checks ProjectScanner runs (spec 01, "Editor tools") and how serious each one is.</summary>
    public static class ScanRules
    {
        /// <summary>A component whose script no longer exists (deleted, renamed or failed to compile).</summary>
        public const string MissingScript = "missing_script";
        /// <summary>An object field that was set, but its target is gone ("Missing" in the Inspector).</summary>
        public const string BrokenReference = "broken_reference";
        /// <summary>A script's object field that was never set ("None"). Often intended, so info only.</summary>
        public const string UnassignedReference = "unassigned_reference";
        /// <summary>A renderer material slot that is empty or points at a deleted material (renders magenta).</summary>
        public const string NullMaterial = "null_material";
        /// <summary>A material whose shader is missing or failed to compile (Unity's error shader, magenta).</summary>
        public const string ErrorShader = "error_shader";
        /// <summary>A scene listed in Build Settings whose file is gone (the build would fail).</summary>
        public const string MissingScene = "missing_scene";

        /// <summary>What Unity puts on a material when its shader can't be used.</summary>
        public const string ErrorShaderName = "Hidden/InternalErrorShader";

        public static string SeverityOf(string rule) =>
            rule == UnassignedReference ? ScanSeverity.Info : ScanSeverity.Error;

        /// <summary>
        /// Spec 01: a serialized reference whose value is null but whose instance id is not 0 points at
        /// something that was deleted (broken); id 0 means it was never set (unassigned). Returns null for
        /// a reference that resolves.
        /// </summary>
        public static string ClassifyReference(bool valueIsNull, int instanceId)
        {
            if (!valueIsNull) return null;
            return instanceId != 0 ? BrokenReference : UnassignedReference;
        }

        /// <summary>True for no shader at all or Unity's error shader.</summary>
        public static bool IsErrorShader(string shaderName) =>
            string.IsNullOrEmpty(shaderName) || shaderName == ErrorShaderName;
    }

    public static class ScanSeverity
    {
        public const string Error = "error";
        public const string Info = "info";
    }

    /// <summary>One problem: which rule, in which asset, on which object, component and field.</summary>
    public sealed class ScanFinding
    {
        [JsonProperty("severity", Order = 0)] public string Severity;
        [JsonProperty("rule", Order = 1)] public string Rule;
        /// <summary>Scene or prefab path, e.g. <c>Assets/Scenes/Level1.unity</c>.</summary>
        [JsonProperty("asset", Order = 2)] public string Asset;
        /// <summary>Hierarchy path inside the asset, e.g. <c>Enemies/Orc_03</c>.</summary>
        [JsonProperty("object", Order = 3, NullValueHandling = NullValueHandling.Ignore)] public string ObjectPath;
        [JsonProperty("component", Order = 4, NullValueHandling = NullValueHandling.Ignore)] public string Component;
        /// <summary>Serialized property path, e.g. <c>target</c> or <c>m_Materials.Array.data[1]</c>.</summary>
        [JsonProperty("property", Order = 5, NullValueHandling = NullValueHandling.Ignore)] public string Property;
        [JsonProperty("detail", Order = 6, NullValueHandling = NullValueHandling.Ignore)] public string Detail;
    }

    /// <summary>
    /// The result of one scan: written as <c>scan.json</c> and summarised on the console. The scan is a
    /// Unity-side report (Python doesn't read it), so it has no schema in <c>schemas/</c>; its format is
    /// documented in docs/USER_GUIDE.md.
    /// </summary>
    public sealed class ScanReport
    {
        public const string Tool = "qalab.project_scanner";
        public const int FormatVersion = 1;

        private readonly List<ScanFinding> _findings = new List<ScanFinding>();

        public ScanReport(string project, string unityVersion, DateTime scannedAtUtc)
        {
            Project = project;
            UnityVersion = unityVersion;
            ScannedAt = Timestamps.Iso(scannedAtUtc);
        }

        public string Project { get; }
        public string UnityVersion { get; }
        public string ScannedAt { get; }
        public int ScenesScanned { get; set; }
        public int PrefabsScanned { get; set; }

        public int Errors { get; private set; }
        public int Infos { get; private set; }

        /// <summary>For <c>-executeMethod</c>: 0 when no error was found, 1 otherwise (spec 01).</summary>
        public int ExitCode => Errors > 0 ? 1 : 0;

        public IReadOnlyList<ScanFinding> Findings => Sorted();

        /// <summary>Record a finding; its severity comes from the rule when not set.</summary>
        public void Add(ScanFinding finding)
        {
            if (finding == null) throw new ArgumentNullException(nameof(finding));
            if (string.IsNullOrEmpty(finding.Rule)) throw new ArgumentException("rule is required", nameof(finding));
            finding.Severity = finding.Severity ?? ScanRules.SeverityOf(finding.Rule);
            if (finding.Severity == ScanSeverity.Error) Errors++;
            else Infos++;
            _findings.Add(finding);
        }

        /// <summary>Errors first, then by asset, object, component, property and rule: stable diffs between scans.</summary>
        private List<ScanFinding> Sorted()
        {
            var sorted = new List<ScanFinding>(_findings);
            sorted.Sort((a, b) =>
            {
                var bySeverity = Rank(a.Severity).CompareTo(Rank(b.Severity));
                if (bySeverity != 0) return bySeverity;
                foreach (var (x, y) in new[]
                {
                    (a.Asset, b.Asset), (a.ObjectPath, b.ObjectPath), (a.Component, b.Component),
                    (a.Property, b.Property), (a.Rule, b.Rule),
                })
                {
                    var c = string.CompareOrdinal(x ?? "", y ?? "");
                    if (c != 0) return c;
                }
                return 0;
            });
            return sorted;
        }

        private static int Rank(string severity) => severity == ScanSeverity.Error ? 0 : 1;

        public JObject ToJObject()
        {
            var findings = new JArray();
            foreach (var f in Sorted()) findings.Add(JObject.FromObject(f));
            return new JObject
            {
                ["tool"] = Tool,
                ["format_version"] = FormatVersion,
                ["project"] = Project,
                ["unity"] = UnityVersion,
                ["scanned_at"] = ScannedAt,
                ["scanned"] = new JObject { ["scenes"] = ScenesScanned, ["prefabs"] = PrefabsScanned },
                ["summary"] = new JObject { ["errors"] = Errors, ["info"] = Infos },
                ["findings"] = findings,
            };
        }

        /// <summary>Indented JSON with LF line endings and a final newline.</summary>
        public string ToJson() => ToJObject().ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";

        /// <summary>Write <c>scan.json</c> (UTF-8 without BOM), creating the folder if needed.</summary>
        public void WriteTo(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
        }

        /// <summary>One summary line, then one line per error (at most <paramref name="maxErrorLines"/>).</summary>
        public IReadOnlyList<string> ConsoleLines(int maxErrorLines)
        {
            var lines = new List<string>
            {
                $"[QALab] scan: {Errors} error(s), {Infos} info in {ScenesScanned} scene(s) and {PrefabsScanned} prefab(s)",
            };
            var shown = 0;
            foreach (var f in Sorted())
            {
                if (f.Severity != ScanSeverity.Error) break;   // errors sort first
                if (shown == maxErrorLines)
                {
                    lines.Add($"[QALab]   ... {Errors - shown} more in scan.json");
                    break;
                }
                lines.Add("[QALab]   " + Describe(f));
                shown++;
            }
            return lines;
        }

        /// <summary><c>broken_reference Assets/Scenes/L1.unity: Enemies/Orc_03 Patrol.target</c></summary>
        public static string Describe(ScanFinding f)
        {
            var where = f.ObjectPath;
            if (f.Component != null) where = (where == null ? "" : where + " ") + f.Component;
            if (f.Property != null) where += "." + f.Property;
            var text = $"{f.Rule} {f.Asset}";
            if (!string.IsNullOrEmpty(where)) text += ": " + where;
            if (f.Detail != null) text += " (" + f.Detail + ")";
            return text;
        }
    }

    /// <summary>Command-line options of <c>ProjectScanner.RunFromCommandLine</c>.</summary>
    public static class ScanCommandLine
    {
        public const string OutFlag = "-qalabScanOut";

        /// <summary>The <c>-qalabScanOut &lt;path&gt;</c> value, or <paramref name="defaultPath"/>; error when the value is missing.</summary>
        public static string OutPath(IList<string> args, string defaultPath, out string error)
        {
            error = null;
            for (var i = 0; i < args.Count; i++)
            {
                if (!string.Equals(args[i], OutFlag, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    error = OutFlag + " needs a file path";
                    return null;
                }
                return args[i + 1];
            }
            return defaultPath;
        }
    }
}
