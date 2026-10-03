using System.IO;
using UnityEditor;
using UnityEngine;

namespace MiawWorks.QALab.Editor
{
    /// <summary>Tools > QA Lab menu items (the full QA Lab window arrives in M4).</summary>
    public static class QALabMenu
    {
        private const string SettingsFolder = "Assets/QALab/Resources";
        private const string SettingsPath = SettingsFolder + "/" + QALabSettings.ResourceName + ".asset";

        [MenuItem("Tools/QA Lab/Create or Select Settings", priority = 1)]
        public static void CreateOrSelectSettings()
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
        }

        [MenuItem("Tools/QA Lab/Open Last Run Folder", priority = 2)]
        public static void OpenLastRunFolder()
        {
            var dir = EditorPrefs.GetString("QALab.LastRunDir", "");
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                Debug.LogWarning("[QALab] no run folder yet: enter Play Mode with QA Lab enabled first");
                return;
            }
            EditorUtility.RevealInFinder(Path.Combine(dir, "run.json"));
        }
    }
}
