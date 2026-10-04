using System;
using System.Collections.Generic;
using System.Linq;
using MiawWorks.QALab.Editor;
using NUnit.Framework;
using UnityEngine;

namespace MiawWorks.QALab.Tests
{
    /// <summary>
    /// ProjectScanner's checks on GameObjects built in code (Unity only; the report format and rules are
    /// covered engine-free in ScanReportTests). Missing scripts can't be created from code, so that check
    /// is verified by hand on a real project (PC checklist).
    /// </summary>
    public class ProjectScannerTests
    {
        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        private T Track<T>(T obj) where T : UnityEngine.Object
        {
            _created.Add(obj);
            return obj;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var obj in _created)
            {
                if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            }
            _created.Clear();
        }

        private static ScanReport Scan(GameObject root)
        {
            var report = new ScanReport("test", Application.unityVersion, DateTime.UtcNow);
            ProjectScanner.ScanHierarchy(root, "Assets/Test.prefab", report);
            return report;
        }

        [Test]
        public void HealthyObjectHasNoFindings()
        {
            var root = Track(new GameObject("Root"));
            var other = Track(new GameObject("Target"));
            root.AddComponent<ScannerProbe>().Target = other.transform;
            var material = Track(new Material(Shader.Find("Sprites/Default")));
            root.AddComponent<MeshRenderer>().sharedMaterial = material;

            var report = Scan(root);

            Assert.AreEqual(0, report.Findings.Count, string.Join("\n", report.Findings.Select(ScanReport.Describe)));
            Assert.AreEqual(0, report.ExitCode);
        }

        [Test]
        public void UnassignedScriptFieldIsInfoAndEmptyMaterialSlotIsAnError()
        {
            var root = Track(new GameObject("Root"));
            var child = new GameObject("Crate_07");
            child.transform.SetParent(root.transform);
            child.AddComponent<ScannerProbe>();   // target left empty
            child.AddComponent<MeshRenderer>().sharedMaterials = new Material[] { null };

            var report = Scan(root);

            var unassigned = report.Findings.Single(f => f.Rule == ScanRules.UnassignedReference);
            Assert.AreEqual(ScanSeverity.Info, unassigned.Severity);
            Assert.AreEqual("Root/Crate_07", unassigned.ObjectPath);
            Assert.AreEqual("ScannerProbe", unassigned.Component);
            Assert.AreEqual("target", unassigned.Property);

            var empty = report.Findings.Single(f => f.Rule == ScanRules.NullMaterial);
            Assert.AreEqual("MeshRenderer", empty.Component);
            Assert.AreEqual("m_Materials.Array.data[0]", empty.Property);
            Assert.AreEqual(1, report.ExitCode);
            Assert.IsFalse(report.Findings.Any(f => f.Component == "MeshRenderer" && f.Rule != ScanRules.NullMaterial),
                "the material slot is reported once, not also as a reference");
        }

        [Test]
        public void ReferenceToADeletedObjectIsBroken()
        {
            var root = Track(new GameObject("Root"));
            var target = new GameObject("Doomed");
            root.AddComponent<ScannerProbe>().Target = target.transform;
            UnityEngine.Object.DestroyImmediate(target);   // the Inspector now shows "Missing (Transform)"

            var report = Scan(root);

            var broken = report.Findings.Single(f => f.Rule == ScanRules.BrokenReference);
            Assert.AreEqual(ScanSeverity.Error, broken.Severity);
            Assert.AreEqual("target", broken.Property);
            Assert.AreEqual(1, report.ExitCode);
        }

        [Test]
        public void MaterialWithTheErrorShaderIsAnError()
        {
            var root = Track(new GameObject("Root"));
            var broken = Track(new Material(Shader.Find(ScanRules.ErrorShaderName)) { name = "Crate_Broken" });
            root.AddComponent<MeshRenderer>().sharedMaterial = broken;

            var finding = Scan(root).Findings.Single();

            Assert.AreEqual(ScanRules.ErrorShader, finding.Rule);
            StringAssert.Contains("Crate_Broken", finding.Detail);
        }
    }
}
