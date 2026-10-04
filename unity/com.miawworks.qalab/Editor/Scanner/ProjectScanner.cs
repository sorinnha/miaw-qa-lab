using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MiawWorks.QALab.Editor
{
    /// <summary>
    /// Static checks on a project (spec 01, "Editor tools", M7): every enabled scene in Build Settings and
    /// every prefab under Assets/ is checked for missing scripts, broken and unassigned references, empty
    /// material slots and error shaders. Results go to <c>scan.json</c> (format in docs/USER_GUIDE.md)
    /// and the console. Nothing is modified: scenes that weren't open are opened additively and closed.
    /// <list type="bullet">
    /// <item>Menu: Tools > QA Lab > Scan Project (writes <c>Logs/qalab/scan.json</c> and shows it).</item>
    /// <item>Batch: <c>Unity.exe -batchmode -projectPath &lt;p&gt; -executeMethod
    /// MiawWorks.QALab.Editor.ProjectScanner.RunFromCommandLine [-qalabScanOut &lt;file&gt;]</c>; exit code
    /// 0 = clean, 1 = errors found, 2 = the scan itself failed (scripts/scan_project.ps1 wraps it).</item>
    /// </list>
    /// </summary>
    public static class ProjectScanner
    {
        private const int ConsoleErrorLines = 50;

        [MenuItem("Tools/QA Lab/Scan Project", priority = 20)]
        public static void ScanFromMenu()
        {
            var path = DefaultOutPath();
            var report = Scan(showProgress: true);
            report.WriteTo(path);
            Print(report, path);
            EditorUtility.RevealInFinder(path);
        }

        /// <summary>Entry point for <c>-executeMethod</c>; always exits the editor with the scan's code.</summary>
        public static void RunFromCommandLine()
        {
            var code = 2;
            try
            {
                var path = ScanCommandLine.OutPath(Environment.GetCommandLineArgs(), DefaultOutPath(), out var error);
                if (error != null)
                {
                    Debug.LogError("[QALab] scan: " + error);
                }
                else
                {
                    var report = Scan(showProgress: false);
                    report.WriteTo(path);
                    Print(report, path);
                    code = report.ExitCode;
                }
            }
            catch (Exception exc)
            {
                Debug.LogError("[QALab] scan failed: " + exc);
            }
            EditorApplication.Exit(code);
        }

        /// <summary>Scan the enabled Build Settings scenes and all prefabs under Assets/.</summary>
        public static ScanReport Scan(bool showProgress)
        {
            var report = new ScanReport(PlayerSettings.productName, Application.unityVersion, DateTime.UtcNow);
            var scenes = EditorBuildSettings.scenes;
            var prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" });
            var total = scenes.Length + prefabGuids.Length;
            var done = 0;
            try
            {
                foreach (var entry in scenes)
                {
                    ShowProgress(showProgress, entry.path, done++, total);
                    if (entry.enabled) ScanScene(entry.path, report);
                }
                foreach (var guid in prefabGuids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    ShowProgress(showProgress, path, done++, total);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null) continue;
                    report.PrefabsScanned++;
                    ScanHierarchy(prefab, path, report);
                }
            }
            finally
            {
                if (showProgress) EditorUtility.ClearProgressBar();
            }
            return report;
        }

        private static void ScanScene(string path, ScanReport report)
        {
            if (!File.Exists(path))
            {
                report.Add(new ScanFinding
                {
                    Rule = ScanRules.MissingScene, Asset = path, Detail = "listed in Build Settings, file not found",
                });
                return;
            }
            // Leave the user's open scenes alone: scan an open scene in place, open the others additively.
            var open = SceneManager.GetSceneByPath(path);
            var wasOpen = open.IsValid() && open.isLoaded;
            var scene = wasOpen ? open : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                report.ScenesScanned++;
                foreach (var root in scene.GetRootGameObjects())
                {
                    ScanHierarchy(root, path, report);
                }
            }
            finally
            {
                if (!wasOpen) EditorSceneManager.CloseScene(scene, true);
            }
        }

        /// <summary>Check one GameObject and all its children (a scene root or a prefab asset).</summary>
        public static void ScanHierarchy(GameObject root, string assetPath, ScanReport report)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var go = transform.gameObject;
                var objectPath = PathOf(transform);
                var missingScripts = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
                if (missingScripts > 0)
                {
                    report.Add(new ScanFinding
                    {
                        Rule = ScanRules.MissingScript, Asset = assetPath, ObjectPath = objectPath,
                        Detail = missingScripts + " component(s) with a missing script",
                    });
                }
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;   // a missing script: reported above
                    var renderer = component as Renderer;
                    ScanReferences(component, renderer != null, assetPath, objectPath, report);
                    if (renderer != null) ScanMaterials(renderer, assetPath, objectPath, report);
                }
            }
        }

        private static void ScanReferences(Component component, bool isRenderer, string assetPath, string objectPath,
            ScanReport report)
        {
            // Unassigned slots are reported for the game's own scripts only: built-in components have many
            // optional ones (a Light's cookie, a Camera's target texture) that are not bugs.
            var reportUnassigned = component is MonoBehaviour;
            using (var serialized = new SerializedObject(component))
            {
                var property = serialized.GetIterator();
                while (property.NextVisible(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    if (property.propertyPath == "m_Script") continue;
                    // Material slots are checked by ScanMaterials (one finding per slot, empty or broken).
                    if (isRenderer && property.propertyPath.StartsWith("m_Materials", StringComparison.Ordinal)) continue;
                    var rule = ScanRules.ClassifyReference(
                        property.objectReferenceValue == null, property.objectReferenceInstanceIDValue);
                    if (rule == null || (rule == ScanRules.UnassignedReference && !reportUnassigned)) continue;
                    report.Add(new ScanFinding
                    {
                        Rule = rule, Asset = assetPath, ObjectPath = objectPath,
                        Component = component.GetType().Name, Property = property.propertyPath,
                    });
                }
            }
        }

        private static void ScanMaterials(Renderer renderer, string assetPath, string objectPath, ScanReport report)
        {
            var materials = renderer.sharedMaterials;
            // A particle renderer's second slot is the trail material, empty unless trails are used.
            var slots = renderer is ParticleSystemRenderer ? Math.Min(1, materials.Length) : materials.Length;
            for (var i = 0; i < slots; i++)
            {
                var material = materials[i];
                var finding = new ScanFinding
                {
                    Asset = assetPath, ObjectPath = objectPath, Component = renderer.GetType().Name,
                    Property = $"m_Materials.Array.data[{i}]",
                };
                if (material == null)
                {
                    finding.Rule = ScanRules.NullMaterial;
                    report.Add(finding);
                }
                else if (ScanRules.IsErrorShader(material.shader == null ? null : material.shader.name))
                {
                    finding.Rule = ScanRules.ErrorShader;
                    finding.Detail = $"material '{material.name}'";
                    report.Add(finding);
                }
            }
        }

        private static string PathOf(Transform transform)
        {
            var names = new List<string>();
            for (var t = transform; t != null; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static void ShowProgress(bool show, string path, int done, int total)
        {
            if (show) EditorUtility.DisplayProgressBar("QA Lab: scanning project", path, total == 0 ? 1f : (float)done / total);
        }

        private static void Print(ScanReport report, string path)
        {
            var text = string.Join("\n", report.ConsoleLines(ConsoleErrorLines)) + "\n[QALab] scan.json: " + path;
            if (report.Errors > 0) Debug.LogWarning(text);
            else Debug.Log(text);
        }

        /// <summary><c>&lt;project&gt;/Logs/qalab/scan.json</c>: Unity's Logs folder is outside Assets and gitignored.</summary>
        private static string DefaultOutPath() =>
            Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "qalab", "scan.json");
    }
}
