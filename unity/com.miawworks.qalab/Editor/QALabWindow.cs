using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiawWorks.QALab.Editor
{
    /// <summary>
    /// Tools > QA Lab > Window (spec 01, "Editor tools"): the settings asset, "Play with QA Lab", the last
    /// run (folder, results, report), and shortcuts to build the playtest player and scan the project.
    /// </summary>
    public sealed class QALabWindow : EditorWindow
    {
        private UnityEditor.Editor _settingsEditor;
        private Vector2 _scroll;

        [MenuItem("Tools/QA Lab/Window", priority = 0)]
        public static void Open() => GetWindow<QALabWindow>("QA Lab").Show();

        private void OnDisable()
        {
            if (_settingsEditor != null) DestroyImmediate(_settingsEditor);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.LabelField("QA Lab " + QALab.Version, EditorStyles.boldLabel);
            DrawSettings();
            EditorGUILayout.Space();
            DrawPlay();
            EditorGUILayout.Space();
            DrawLastRun();
            EditorGUILayout.Space();
            DrawTools();
            EditorGUILayout.EndScrollView();
        }

        private void DrawSettings()
        {
            var settings = QALabMenu.LoadSettings();
            if (settings == null)
            {
                EditorGUILayout.HelpBox("No settings asset yet. Play Mode runs need one (builds use command-line flags).", MessageType.Info);
                if (GUILayout.Button("Create settings")) QALabMenu.CreateOrSelectSettings();
                return;
            }
            UnityEditor.Editor.CreateCachedEditor(settings, null, ref _settingsEditor);
            _settingsEditor.OnInspectorGUI();
            EditorGUILayout.LabelField("Bots", string.Join(", ", BotAdapterRegistry.Names) + ", manual", EditorStyles.miniLabel);
        }

        private static void DrawPlay()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Play with QA Lab", GUILayout.Height(28)))
                {
                    var settings = QALabMenu.LoadSettings() ?? QALabMenu.CreateOrSelectSettings();
                    var so = new SerializedObject(settings);
                    so.FindProperty("autoStartInPlayMode").boolValue = true;
                    so.ApplyModifiedProperties();
                    EditorApplication.isPlaying = true;
                }
            }
            if (QALab.IsRunning)
            {
                EditorGUILayout.HelpBox("Recording: " + QALab.RunDir, MessageType.None);
                if (GUILayout.Button("End run now")) QALab.EndRun(ExitReasons.UserQuit);
            }
        }

        private static void DrawLastRun()
        {
            EditorGUILayout.LabelField("Last run", EditorStyles.boldLabel);
            var dir = EditorPrefs.GetString(QALabMenu.LastRunDirPref, "");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                EditorGUILayout.LabelField("none yet", EditorStyles.miniLabel);
                return;
            }
            EditorGUILayout.SelectableLabel(dir, EditorStyles.miniLabel, GUILayout.Height(16));
            foreach (var line in RunSummary.Lines(Path.Combine(dir, "results.xml")))
            {
                EditorGUILayout.LabelField(line, EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open run folder")) QALabMenu.OpenLastRunFolder();
                var report = RunSummary.ReportFor(dir);
                using (new EditorGUI.DisabledScope(report == null))
                {
                    if (GUILayout.Button(report == null ? "No report yet" : "Open report")) Application.OpenURL("file:///" + report.Replace('\\', '/'));
                }
            }
            if (RunSummary.ReportFor(dir) == null)
            {
                EditorGUILayout.HelpBox("Make the report with: qalab triage run \"" + dir + "\" --provider fake --out reports/" + Path.GetFileName(dir), MessageType.None);
            }
        }

        private static void DrawTools()
        {
            EditorGUILayout.LabelField("Tools", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("Build playtest player")) BuildRunner.BuildFromMenu();
                if (GUILayout.Button("Scan project (missing scripts, broken references)")) ProjectScanner.ScanFromMenu();
            }
        }
    }
}
