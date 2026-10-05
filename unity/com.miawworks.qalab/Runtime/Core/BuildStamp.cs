// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// The commit a player was built from. <c>BuildRunner</c> writes <c>qalab_build.json</c>
    /// (<c>{"git_sha": "..."}</c>) into the build's StreamingAssets folder; run.json → build.git_sha reads
    /// it back, so every run says exactly which code it tested.
    /// </summary>
    public static class BuildStamp
    {
        public const string FileName = "qalab_build.json";

        private static readonly Regex Sha = new Regex("^[0-9a-f]{7,40}$", RegexOptions.CultureInvariant);

        public static bool IsSha(string value) => value != null && Sha.IsMatch(value);

        /// <summary>The stamp's JSON text for <paramref name="gitSha"/> (null when unknown).</summary>
        public static string ToJson(string gitSha) =>
            new JObject { ["git_sha"] = IsSha(gitSha) ? gitSha : null }.ToString(Newtonsoft.Json.Formatting.None) + "\n";

        /// <summary>The sha in <paramref name="folder"/>/qalab_build.json, or null (no file, bad content).</summary>
        public static string ReadGitSha(string folder)
        {
            try
            {
                var path = Path.Combine(folder ?? string.Empty, FileName);
                if (!File.Exists(path)) return null;
                var sha = (string)JObject.Parse(File.ReadAllText(path))["git_sha"];
                return IsSha(sha) ? sha : null;
            }
            catch (Exception)
            {
                return null;   // a broken stamp must never stop a run
            }
        }
    }
}
