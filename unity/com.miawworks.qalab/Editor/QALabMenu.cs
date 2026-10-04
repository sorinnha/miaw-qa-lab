using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiawWorks.QALab.Editor
{
    /// <summary>Tools > QA Lab menu items (the QA Lab window has the same actions).</summary>
    public static class QALabMenu
    {
        public const string LastRunDirPref = "QALab.LastRunDir";
        private const string SettingsFolder = "Assets/QALab/Resources";
        private const string SettingsPath = SettingsFolder + "/" + QALabSettings.ResourceName + ".asset";

        /// <summary>The settings asset, or null when it doesn't exist yet.</summary>
        public static QALabSettings LoadSettings() => AssetDatabase.LoadAssetAtPath<QALabSettings>(SettingsPath);

        [MenuItem("Tools/QA Lab/Create or Select Settings", priority = 1)]
        public static void CreateOrSelectSettingsMenu() => CreateOrSelectSettings();

        /// <summary>Create the settings asset if needed, select it, and return it.</summary>
        public static QALabSettings CreateOrSelectSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<QALabSettings>(SettingsPath);
            if (settings == null)
            {
                Directory.CreateDirectory(SettingsFolder);
                settings = ScriptableObject.CreateInstance<QALabSettings>();
                AssetDatabase.CreateAsset(settings, SettingsPath);
                AssetDatabase.SaveAssets();
                Debug.Log("[QALab] created " + SettingsPath);
            }
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
            return settings;
        }

        [MenuItem("Tools/QA Lab/Open Last Run Folder", priority = 2)]
        public static void OpenLastRunFolder()
        {
            var dir = EditorPrefs.GetString(LastRunDirPref, "");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Debug.LogWarning("[QALab] no run folder yet: enter Play Mode with QA Lab enabled first");
                return;
            }
            EditorUtility.RevealInFinder(Path.Combine(dir, "run.json"));
        }
    }
}
