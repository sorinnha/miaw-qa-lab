// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;

namespace MiawWorks.QALab.Editor
{
    /// <summary>What the QA Lab window shows about the last run: its results.xml in a few lines, and where its report is.</summary>
    public static class RunSummary
    {
        /// <summary>
        /// One line per problem in results.xml (<c>FAIL fell_out_of_world: 2 ... event(s)</c>), or a single
        /// "all N checks passed" line. Empty when the file is missing or unreadable.
        /// </summary>
        public static List<string> Lines(string resultsXmlPath)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(resultsXmlPath) || !File.Exists(resultsXmlPath)) return lines;
            XDocument doc;
            try
            {
                doc = XDocument.Load(resultsXmlPath);
            }
            catch (Exception)
            {
                return lines;
            }
            var total = 0;
            foreach (var testCase in doc.Descendants("testcase"))
            {
                total++;
                var name = (string)testCase.Attribute("name");
                foreach (var (element, tag) in new[] { ("failure", "FAIL"), ("error", "ERROR"), ("skipped", "SKIP") })
                {
                    var problem = testCase.Element(element);
                    if (problem != null) lines.Add($"{tag} {name}: {(string)problem.Attribute("message")}");
                }
            }
            if (lines.Count == 0 && total > 0) lines.Add($"all {total} checks passed");
            return lines;
        }

        /// <summary>
        /// The triage report for a run folder, by the convention scripts/run_pipeline.ps1 uses:
        /// <c>&lt;runs&gt;/&lt;run_id&gt;</c> → <c>&lt;runs&gt;/../reports/&lt;run_id&gt;/report.html</c>. Null when it doesn't exist.
        /// </summary>
        public static string ReportFor(string runDir)
        {
            if (string.IsNullOrEmpty(runDir)) return null;
            var full = Path.GetFullPath(runDir.TrimEnd('/', '\\'));
            var runsDir = Path.GetDirectoryName(full);
            if (runsDir == null) return null;
            var root = Path.GetDirectoryName(runsDir);
            if (root == null) return null;
            var report = Path.Combine(root, "reports", Path.GetFileName(full), "report.html");
            return File.Exists(report) ? report : null;
        }
    }
}
