// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;

namespace MiawWorks.QALab
{
    /// <summary>One detector's line in results.xml.</summary>
    public sealed class DetectorResult
    {
        public string Name;
        public int Count;
        /// <summary>Worst severity reported, or null when it never fired.</summary>
        public string WorstSeverity;
        /// <summary>Why the detector was skipped (not implemented yet), or null.</summary>
        public string Skipped;
        /// <summary>The error that turned the detector off (a QA Lab internal error), or null.</summary>
        public string Error;
    }

    /// <summary>What results.xml reports about a run.</summary>
    public sealed class RunResults
    {
        public string RunId;
        public int Seed;
        public string Adapter;
        public double DurationS;
        public DateTime StartedAtUtc;
        public int ExitCode;
        public List<DetectorResult> Detectors = new List<DetectorResult>();
        /// <summary>Exception-level log events in the run.</summary>
        public long Exceptions;
        /// <summary>QA Lab failures outside the detectors (bot, captures, the host), one line each. Detector failures are in <see cref="Detectors"/>.</summary>
        public List<string> InternalErrors = new List<string>();

        /// <summary>
        /// The hub's view of the run: every built-in detector (so results.xml always has the same test
        /// cases), then any game detector that reported or was registered.
        /// </summary>
        public void AddDetectorsFrom(DetectorHub hub)
        {
            var names = new List<string>(DetectorNames.BuiltIn);
            foreach (var detector in hub.Detectors)
            {
                if (!names.Contains(detector.Name)) names.Add(detector.Name);
            }
            foreach (var name in hub.Counts.Keys)
            {
                if (!names.Contains(name)) names.Add(name);
            }
            foreach (var name in names)
            {
                hub.Skipped.TryGetValue(name, out var skipped);
                hub.Failed.TryGetValue(name, out var error);
                Detectors.Add(new DetectorResult
                {
                    Name = name,
                    Count = hub.Count(name),
                    WorstSeverity = hub.WorstSeverity(name),
                    Skipped = skipped,
                    Error = error,
                });
            }
        }
    }

    /// <summary>
    /// Writes results.xml (spec 01): JUnit XML that Jenkins, TeamCity and GitHub test reporters read.
    /// One test case per detector (a failure lists how many events it wrote), one for "no exceptions",
    /// and one for QA Lab's own health (an error, not a failure, when QA Lab broke). A detector that
    /// wasn't implemented yet is skipped.
    /// </summary>
    public static class JUnitWriter
    {
        public static string ToXml(RunResults results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int tests = 0, failures = 0, errors = 0, skipped = 0;
            foreach (var d in results.Detectors)
            {
                tests++;
                if (d.Error != null) errors++;
                else if (d.Skipped != null) skipped++;
                else if (d.Count > 0) failures++;
            }
            tests += 2;
            if (results.Exceptions > 0) failures++;
            if (results.InternalErrors.Count > 0) errors++;

            var text = new StringBuilder();
            var settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
                OmitXmlDeclaration = true,
            };
            text.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n");
            using (var xml = XmlWriter.Create(text, settings))
            {
                var time = Seconds(results.DurationS);
                xml.WriteStartElement("testsuites");
                xml.WriteAttributeString("name", "qalab");
                WriteCounts(xml, tests, failures, errors, skipped, time);

                xml.WriteStartElement("testsuite");
                xml.WriteAttributeString("name", "qalab." + (results.RunId ?? "run"));
                WriteCounts(xml, tests, failures, errors, skipped, time);
                xml.WriteAttributeString("timestamp", results.StartedAtUtc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));

                xml.WriteStartElement("properties");
                Property(xml, "run_id", results.RunId);
                Property(xml, "seed", results.Seed.ToString(CultureInfo.InvariantCulture));
                Property(xml, "adapter", results.Adapter);
                Property(xml, "exit_code", results.ExitCode.ToString(CultureInfo.InvariantCulture));
                xml.WriteEndElement();

                foreach (var d in results.Detectors)
                {
                    StartCase(xml, "qalab.detectors", d.Name);
                    if (d.Error != null)
                    {
                        Problem(xml, "error", "detector failed: " + d.Error, "internal", $"{d.Count} event(s) before it failed");
                    }
                    else if (d.Skipped != null)
                    {
                        xml.WriteStartElement("skipped");
                        xml.WriteAttributeString("message", d.Skipped);
                        xml.WriteEndElement();
                    }
                    else if (d.Count > 0)
                    {
                        Problem(xml, "failure", $"{d.Count} {d.Name} event(s), worst severity {d.WorstSeverity}",
                            d.WorstSeverity ?? "unknown", "See events.jsonl (kind \"detector\") and the screenshots they point to.");
                    }
                    xml.WriteEndElement();
                }

                StartCase(xml, "qalab.logs", "no_exceptions");
                if (results.Exceptions > 0)
                {
                    Problem(xml, "failure", $"{results.Exceptions} exception(s) logged", "exception",
                        "See events.jsonl (level \"exception\"), or run qalab triage for grouped reports.");
                }
                xml.WriteEndElement();

                StartCase(xml, "qalab.internal", "no_internal_errors");
                if (results.InternalErrors.Count > 0)
                {
                    Problem(xml, "error", $"{results.InternalErrors.Count} QA Lab internal error(s)", "internal",
                        string.Join("\n", results.InternalErrors));
                }
                xml.WriteEndElement();

                xml.WriteEndElement();   // testsuite
                xml.WriteEndElement();   // testsuites
            }
            return text.ToString().Replace("\r\n", "\n") + "\n";
        }

        /// <summary>Write results.xml: UTF-8 without BOM, LF line endings.</summary>
        public static void WriteTo(string path, RunResults results) =>
            File.WriteAllText(path, ToXml(results), new UTF8Encoding(false));

        private static string Seconds(double s) => Math.Round(s, 3).ToString("0.###", CultureInfo.InvariantCulture);

        private static void WriteCounts(XmlWriter xml, int tests, int failures, int errors, int skipped, string time)
        {
            xml.WriteAttributeString("tests", tests.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("failures", failures.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("errors", errors.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("skipped", skipped.ToString(CultureInfo.InvariantCulture));
            xml.WriteAttributeString("time", time);
        }

        private static void Property(XmlWriter xml, string name, string value)
        {
            xml.WriteStartElement("property");
            xml.WriteAttributeString("name", name);
            xml.WriteAttributeString("value", value ?? string.Empty);
            xml.WriteEndElement();
        }

        private static void StartCase(XmlWriter xml, string classname, string name)
        {
            xml.WriteStartElement("testcase");
            xml.WriteAttributeString("classname", classname);
            xml.WriteAttributeString("name", name);
            xml.WriteAttributeString("time", "0");
        }

        private static void Problem(XmlWriter xml, string element, string message, string type, string body)
        {
            xml.WriteStartElement(element);
            xml.WriteAttributeString("message", message);
            xml.WriteAttributeString("type", type);
            xml.WriteString(body);
            xml.WriteEndElement();
        }
    }
}
