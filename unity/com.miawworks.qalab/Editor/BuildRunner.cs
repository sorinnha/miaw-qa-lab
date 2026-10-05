using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MiawWorks.QALab.Editor
{
    /// <summary>
    /// Builds the windowed development player that playtests run in (spec 01, "Build and run"):
    /// Development, Mono, the scenes enabled in Build Settings. From the command line:
    /// <code>Unity.exe -batchmode -quit -projectPath unity/QALabSandbox -executeMethod MiawWorks.QALab.Editor.BuildRunner.BuildSandboxPlayer [-qalabBuildOut path/QALabSandbox.exe]</code>
    /// Exits 0 on success and 1 on failure. The commit is stamped into StreamingAssets/qalab_build.json so
    /// every run's run.json says which code it tested (build.git_sha).
    /// </summary>
    public static class BuildRunner
    {
        public const string OutFlag = "-qalabBuildOut";

        /// <summary>
        /// The sandbox (project folder <c>QALabSandbox</c>): <c>Builds/Sandbox/QALabSandbox.exe</c> at the repo
        /// root, where scripts/run_playtest.ps1 looks. Any other project: <c>&lt;project&gt;/Builds/QALab/&lt;product&gt;.exe</c>.
        /// </summary>
        public static string DefaultOutput
        {
            get
            {
                var project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                if (Path.GetFileName(project) == "QALabSandbox")
                {
                    return Path.GetFullPath(Path.Combine(project, "..", "..", "Builds", "Sandbox", "QALabSandbox.exe"));
                }
                return Path.Combine(project, "Builds", "QALab", PlayerSettings.productName + ".exe");
            }
        }

        [MenuItem("Tools/QA Lab/Build Playtest Player", priority = 30)]
        public static void BuildFromMenu()
        {
            var ok = Build(DefaultOutput);
            if (ok) EditorUtility.RevealInFinder(DefaultOutput);
        }

        /// <summary>Entry point for <c>-executeMethod</c>. Quits the editor with 0 (built) or 1 (failed).</summary>
        public static void BuildSandboxPlayer()
        {
            var exitCode = 1;
            try
            {
                var output = EditorCommandLine.Value(Environment.GetCommandLineArgs(), OutFlag, DefaultOutput, out var error);
                if (error != null)
                {
                    Debug.LogError("[QALab] " + error);
                }
                else
                {
                    exitCode = Build(Path.GetFullPath(output)) ? 0 : 1;
                }
            }
            catch (Exception exc)
            {
                Debug.LogError("[QALab] build failed: " + exc);
            }
            if (Application.isBatchMode) EditorApplication.Exit(exitCode);
        }

        /// <summary>Build the Windows development player to <paramref name="output"/>. True on success.</summary>
        public static bool Build(string output)
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && File.Exists(scene.path)) scenes.Add(scene.path);
            }
            if (scenes.Count == 0)
            {
                Debug.LogError("[QALab] no enabled scenes in Build Settings (sandbox: Tools > QA Lab > Rebuild Sandbox Scenes)");
                return false;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output));

            // Mono builds start fast and keep full managed stack traces; IL2CPP is for shipping.
            var target = NamedBuildTarget.Standalone;
            var previousBackend = PlayerSettings.GetScriptingBackend(target);
            PlayerSettings.SetScriptingBackend(target, ScriptingImplementation.Mono2x);
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes.ToArray(),
                    locationPathName = output,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.Development,
                });
                var summary = report.summary;
                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[QALab] build {summary.result}: {summary.totalErrors} error(s), see the editor log");
                    return false;
                }
                StampCommit(output);
                Debug.Log($"[QALab] built {output} ({summary.totalSize / (1024 * 1024)} MB, {summary.totalTime.TotalSeconds:0} s)");
                return true;
            }
            finally
            {
                PlayerSettings.SetScriptingBackend(target, previousBackend);
            }
        }

        // <exe dir>/<exe name>_Data/StreamingAssets/qalab_build.json, read back by RunContext at run start.
        private static void StampCommit(string output)
        {
            var dataDir = Path.Combine(Path.GetDirectoryName(output), Path.GetFileNameWithoutExtension(output) + "_Data", "StreamingAssets");
            Directory.CreateDirectory(dataDir);
            File.WriteAllText(Path.Combine(dataDir, BuildStamp.FileName), BuildStamp.ToJson(GitSha()), new System.Text.UTF8Encoding(false));
        }

        /// <summary>The commit being built: <c>GIT_COMMIT</c> (Jenkins) if set, else <c>git rev-parse HEAD</c>, else null.</summary>
        private static string GitSha()
        {
            var fromCi = Environment.GetEnvironmentVariable("GIT_COMMIT");
            if (BuildStamp.IsSha(fromCi)) return fromCi;
            try
            {
                var start = new ProcessStartInfo("git", "rev-parse HEAD")
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                };
                using (var git = Process.Start(start))
                {
                    var sha = git.StandardOutput.ReadToEnd().Trim();
                    git.WaitForExit(5000);
                    return BuildStamp.IsSha(sha) ? sha : null;
                }
            }
            catch (Exception)
            {
                return null;   // no git on PATH: the run just says git_sha null
            }
        }
    }
}
