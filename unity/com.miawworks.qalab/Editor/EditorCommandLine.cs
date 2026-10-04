// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;

namespace MiawWorks.QALab.Editor
{
    /// <summary>Flags for the editor's <c>-executeMethod</c> entry points (BuildRunner, ProjectScanner).</summary>
    public static class EditorCommandLine
    {
        /// <summary>
        /// The value after <paramref name="flag"/> (case-insensitive), or <paramref name="defaultValue"/> when
        /// the flag is absent. A flag at the end or followed by another flag sets <paramref name="error"/>.
        /// </summary>
        public static string Value(IList<string> args, string flag, string defaultValue, out string error)
        {
            error = null;
            for (var i = 0; i < args.Count; i++)
            {
                if (!string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) continue;
                if (i + 1 >= args.Count || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    error = flag + ": missing value";
                    return null;
                }
                return args[i + 1];
            }
            return defaultValue;
        }
    }
}
