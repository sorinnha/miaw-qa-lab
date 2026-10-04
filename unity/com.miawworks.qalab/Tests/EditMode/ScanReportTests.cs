// Engine-free: runs in Unity's Test Runner and in tools/cs-check.
using System;
using System.IO;
using System.Linq;
using System.Text;
using MiawWorks.QALab.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    /// <summary>Spec 01 ProjectScanner rules, scan.json and the console summary (the engine-free half).</summary>
    public class ScanReportTests
    {
        private static ScanReport NewReport() =>
            new ScanReport("QALabSandbox", "6000.0.23f1", new DateTime(2026, 10, 5, 10, 30, 0, DateTimeKind.Utc));

        [Test]
        public void ReferencesAreBrokenUnassignedOrFine()
        {
            Assert.AreEqual(ScanRules.BrokenReference, ScanRules.ClassifyReference(true, 12345));
            Assert.AreEqual(ScanRules.BrokenReference, ScanRules.ClassifyReference(true, -4));
            Assert.AreEqual(ScanRules.UnassignedReference, ScanRules.ClassifyReference(true, 0));
            Assert.IsNull(ScanRules.ClassifyReference(false, 12345));
        }

        [Test]
        public void OnlyUnassignedReferencesAreInfo()
        {
            Assert.AreEqual(ScanSeverity.Info, ScanRules.SeverityOf(ScanRules.UnassignedReference));
            foreach (var rule in new[]
            {
                ScanRules.MissingScript, ScanRules.BrokenReference, ScanRules.NullMaterial,
                ScanRules.ErrorShader, ScanRules.MissingScene,
            })
            {
                Assert.AreEqual(ScanSeverity.Error, ScanRules.SeverityOf(rule), rule);
            }
        }

        [TestCase("Assets/Scripts/Patrol.cs", true)]
        [TestCase("Assets/Plugins/ThirdParty/Spawner.cs", true)]
        [TestCase("Packages/com.miawworks.qalab/Tests/EditMode/ScannerProbe.cs", true)]
        [TestCase("Packages/com.unity.ugui/Runtime/UI/Core/Image.cs", false)]
        [TestCase("Packages/com.unity.textmeshpro/Scripts/Runtime/TMP_Text.cs", false)]
        [TestCase("Packages/com.unity.ai.navigation/Runtime/NavMeshSurface.cs", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void UnassignedFieldsAreListedForScriptsOutsideUnitysPackages(string scriptPath, bool expected)
        {
            Assert.AreEqual(expected, ScanRules.ReportsUnassigned(scriptPath));
        }

        [Test]
        public void ErrorShaderIsUnitysErrorShaderOrNone()
        {
            Assert.IsTrue(ScanRules.IsErrorShader("Hidden/InternalErrorShader"));
            Assert.IsTrue(ScanRules.IsErrorShader(null));
            Assert.IsTrue(ScanRules.IsErrorShader(""));
            Assert.IsFalse(ScanRules.IsErrorShader("Universal Render Pipeline/Lit"));
        }

        [Test]
        public void CleanScanExitsZero()
        {
            var report = NewReport();
            report.Add(new ScanFinding { Rule = ScanRules.UnassignedReference, Asset = "Assets/A.prefab" });
            Assert.AreEqual(0, report.Errors);
            Assert.AreEqual(1, report.Infos);
            Assert.AreEqual(0, report.ExitCode, "info findings alone don't fail the scan");
        }

        [Test]
        public void ScanJsonHasTheDocumentedShapeWithErrorsFirstInAStableOrder()
        {
            var report = NewReport();
            report.ScenesScanned = 2;
            report.PrefabsScanned = 14;
            report.Add(new ScanFinding
            {
                Rule = ScanRules.UnassignedReference, Asset = "Assets/A.prefab", ObjectPath = "A", Component = "Door",
                Property = "hinge",
            });
            report.Add(new ScanFinding
            {
                Rule = ScanRules.NullMaterial, Asset = "Assets/Scenes/L1.unity", ObjectPath = "Level/Crate_07",
                Component = "MeshRenderer", Property = "m_Materials.Array.data[0]",
            });
            report.Add(new ScanFinding { Rule = ScanRules.MissingScript, Asset = "Assets/B.prefab", ObjectPath = "B", Detail = "1 component(s) with a missing script" });

            Assert.AreEqual(1, report.ExitCode);
            var json = (JObject)Json.Parse(report.ToJson());
            CollectionAssert.AreEqual(
                new[] { "tool", "format_version", "project", "unity", "scanned_at", "scanned", "summary", "findings" },
                json.Properties().Select(p => p.Name));
            Assert.AreEqual("qalab.project_scanner", (string)json["tool"]);
            Assert.AreEqual(1, (int)json["format_version"]);
            Assert.AreEqual("2026-10-05T10:30:00.000Z", (string)json["scanned_at"]);
            Assert.AreEqual(2, (int)json["scanned"]["scenes"]);
            Assert.AreEqual(14, (int)json["scanned"]["prefabs"]);
            Assert.AreEqual(2, (int)json["summary"]["errors"]);
            Assert.AreEqual(1, (int)json["summary"]["info"]);

            var findings = (JArray)json["findings"];
            CollectionAssert.AreEqual(
                new[] { "missing_script", "null_material", "unassigned_reference" },
                findings.Select(f => (string)f["rule"]));
            Assert.AreEqual("error", (string)findings[0]["severity"]);
            Assert.IsNull(findings[0]["component"], "unset fields are left out");
            Assert.AreEqual("m_Materials.Array.data[0]", (string)findings[1]["property"]);
        }

        [Test]
        public void ConsoleLinesSummariseAndCapTheErrorList()
        {
            var report = NewReport();
            report.ScenesScanned = 1;
            for (var i = 0; i < 5; i++)
            {
                report.Add(new ScanFinding
                {
                    Rule = ScanRules.BrokenReference, Asset = "Assets/Scenes/L1.unity", ObjectPath = "Enemies/Orc_0" + i,
                    Component = "Patrol", Property = "target",
                });
            }
            report.Add(new ScanFinding { Rule = ScanRules.UnassignedReference, Asset = "Assets/A.prefab" });

            var lines = report.ConsoleLines(3);
            Assert.AreEqual("[QALab] scan: 5 error(s), 1 info in 1 scene(s) and 0 prefab(s)", lines[0]);
            Assert.AreEqual("[QALab]   broken_reference Assets/Scenes/L1.unity: Enemies/Orc_00 Patrol.target", lines[1]);
            Assert.AreEqual("[QALab]   ... 2 more in scan.json", lines[4]);
            Assert.AreEqual(5, lines.Count, "info findings are in scan.json only");
            Assert.AreEqual(6, report.ConsoleLines(5).Count, "exactly the cap: no 'more' line");
        }

        [Test]
        public void DescribeHandlesFindingsWithoutObjectOrComponent()
        {
            var missingScene = new ScanFinding
            {
                Rule = ScanRules.MissingScene, Asset = "Assets/Scenes/Old.unity", Detail = "listed in Build Settings, file not found",
            };
            Assert.AreEqual("missing_scene Assets/Scenes/Old.unity (listed in Build Settings, file not found)",
                ScanReport.Describe(missingScene));
        }

        [Test]
        public void AFindingNeedsARule()
        {
            Assert.Throws<ArgumentException>(() => NewReport().Add(new ScanFinding { Asset = "Assets/A.prefab" }));
            Assert.Throws<ArgumentNullException>(() => NewReport().Add(null));
        }

        [Test]
        public void WriteToCreatesTheFolderAndWritesUtf8WithoutBomAndLf()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qalab-scan-" + Guid.NewGuid().ToString("N"));
            try
            {
                var path = Path.Combine(dir, "nested", "scan.json");
                NewReport().WriteTo(path);
                var bytes = File.ReadAllBytes(path);
                Assert.AreNotEqual(0xEF, bytes[0], "no BOM");
                var text = Encoding.UTF8.GetString(bytes);
                StringAssert.DoesNotContain("\r", text);
                StringAssert.EndsWith("}\n", text);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        [TestCase(new string[0], "default.json", null)]
        [TestCase(new[] { "Unity.exe", "-batchmode", "-qalabScanOut", "out/scan.json" }, "out/scan.json", null)]
        [TestCase(new[] { "-QALABSCANOUT", "x.json" }, "x.json", null)]
        public void OutPathComesFromTheFlagOrTheDefault(string[] args, string expected, string expectedError)
        {
            Assert.AreEqual(expected, ScanCommandLine.OutPath(args, "default.json", out var error));
            Assert.AreEqual(expectedError, error);
        }

        [TestCase("-qalabScanOut")]
        [TestCase("-qalabScanOut", "-logFile")]
        public void OutFlagWithoutAValueIsAnError(params string[] args)
        {
            Assert.IsNull(ScanCommandLine.OutPath(args, "default.json", out var error));
            StringAssert.Contains("-qalabScanOut", error);
        }
    }
}
