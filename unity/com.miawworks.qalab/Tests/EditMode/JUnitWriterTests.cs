// Engine-free: runs in Unity's Test Runner and in tools/cs-check.
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using MiawWorks.QALab;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    /// <summary>Spec 01 results.xml: one test case per detector type and one for "no exceptions".</summary>
    public class JUnitWriterTests
    {
        private static RunResults Results()
        {
            var results = new RunResults
            {
                RunId = "20261005T103000Z-s42",
                Seed = 42,
                Adapter = "navmesh_explorer",
                DurationS = 120.04,
                StartedAtUtc = new DateTime(2026, 10, 5, 10, 30, 0, DateTimeKind.Utc),
                ExitCode = 1,
                Exceptions = 3,
            };
            results.Detectors.Add(new DetectorResult { Name = "stuck", Skipped = "not implemented yet" });
            results.Detectors.Add(new DetectorResult { Name = "fell_out_of_world", Count = 2, WorstSeverity = "critical" });
            results.Detectors.Add(new DetectorResult { Name = "perf_spike" });
            results.Detectors.Add(new DetectorResult { Name = "my_detector", Count = 1, Error = "InvalidOperationException: boom" });
            return results;
        }

        private static XElement Case(XDocument doc, string name) =>
            doc.Descendants("testcase").Single(c => (string)c.Attribute("name") == name);

        [Test]
        public void CountsAndCasesFollowTheResults()
        {
            var doc = XDocument.Parse(JUnitWriter.ToXml(Results()));
            var suites = doc.Root;
            Assert.AreEqual("testsuites", suites.Name.LocalName);
            var suite = suites.Element("testsuite");
            foreach (var element in new[] { suites, suite })
            {
                Assert.AreEqual("6", (string)element.Attribute("tests"), "4 detectors + no_exceptions + no_internal_errors");
                Assert.AreEqual("2", (string)element.Attribute("failures"), "fell_out_of_world and no_exceptions");
                Assert.AreEqual("1", (string)element.Attribute("errors"), "my_detector broke");
                Assert.AreEqual("1", (string)element.Attribute("skipped"), "stuck isn't written yet");
                Assert.AreEqual("120.04", (string)element.Attribute("time"));
            }
            Assert.AreEqual("qalab.20261005T103000Z-s42", (string)suite.Attribute("name"));
            Assert.AreEqual("2026-10-05T10:30:00", (string)suite.Attribute("timestamp"));
            var props = suite.Element("properties").Elements("property").ToDictionary(p => (string)p.Attribute("name"), p => (string)p.Attribute("value"));
            Assert.AreEqual("42", props["seed"]);
            Assert.AreEqual("navmesh_explorer", props["adapter"]);
            Assert.AreEqual("1", props["exit_code"]);

            var fell = Case(doc, "fell_out_of_world").Element("failure");
            Assert.AreEqual("2 fell_out_of_world event(s), worst severity critical", (string)fell.Attribute("message"));
            Assert.AreEqual("critical", (string)fell.Attribute("type"));
            Assert.AreEqual("qalab.detectors", (string)Case(doc, "perf_spike").Attribute("classname"));
            Assert.IsFalse(Case(doc, "perf_spike").HasElements, "a detector that never fired passes");
            Assert.AreEqual("not implemented yet", (string)Case(doc, "stuck").Element("skipped").Attribute("message"));
            StringAssert.Contains("boom", (string)Case(doc, "my_detector").Element("error").Attribute("message"));
            Assert.AreEqual("3 exception(s) logged", (string)Case(doc, "no_exceptions").Element("failure").Attribute("message"));
            Assert.IsFalse(Case(doc, "no_internal_errors").HasElements);
        }

        [Test]
        public void ACleanRunHasNoFailures()
        {
            var results = new RunResults { RunId = "r", Adapter = "manual", DurationS = 60 };
            results.Detectors.Add(new DetectorResult { Name = "perf_spike" });
            var doc = XDocument.Parse(JUnitWriter.ToXml(results));
            Assert.AreEqual("3", (string)doc.Root.Attribute("tests"));
            Assert.AreEqual("0", (string)doc.Root.Attribute("failures"));
            Assert.AreEqual("0", (string)doc.Root.Attribute("errors"));
            Assert.AreEqual("60", (string)doc.Root.Attribute("time"));
        }

        [Test]
        public void InternalErrorsAreListedOnePerLine()
        {
            var results = new RunResults { RunId = "r" };
            results.InternalErrors.Add("bot: NullReferenceException: x");
            results.InternalErrors.Add("screenshot: out of memory");
            var error = Case(XDocument.Parse(JUnitWriter.ToXml(results)), "no_internal_errors").Element("error");
            Assert.AreEqual("2 QA Lab internal error(s)", (string)error.Attribute("message"));
            Assert.AreEqual("bot: NullReferenceException: x\nscreenshot: out of memory", error.Value);
        }

        [Test]
        public void ResultsFromAHubListEveryBuiltInDetector()
        {
            var clock = new FakeClock();
            var state = new FakeState { Position = new[] { 0f, 0f, 0f } };
            var hub = new DetectorHub(new EventWriter("r", clock, state, new StringWriter()), clock, state);
            hub.Add(new PerfSpikeDetector());
            hub.Report("perf_spike", "minor");
            hub.Report("custom_check", "trivial");
            var results = new RunResults();
            results.AddDetectorsFrom(hub);
            CollectionAssert.AreEqual(
                new[] { "stuck", "fell_out_of_world", "perf_spike", "exception_burst", "tunneling", "custom_check" },
                results.Detectors.Select(d => d.Name));
            Assert.AreEqual(1, results.Detectors.Single(d => d.Name == "perf_spike").Count);
            Assert.AreEqual("trivial", results.Detectors.Single(d => d.Name == "custom_check").WorstSeverity);
        }

        [Test]
        public void CharactersXmlForbidsAreReplacedNotThrown()
        {
            var results = new RunResults { RunId = "r" };
            results.InternalErrors.Add("bad \u0001 byte and an emoji \U0001F600");
            var error = Case(XDocument.Parse(JUnitWriter.ToXml(results)), "no_internal_errors").Element("error");
            Assert.AreEqual("bad ? byte and an emoji \U0001F600", error.Value);
        }

        [Test]
        public void TextIsEscapedAndTheFileIsUtf8WithoutBomAndLf()
        {
            var results = new RunResults { RunId = "r" };
            results.InternalErrors.Add("a <b> & \"c\"");
            var dir = Path.Combine(Path.GetTempPath(), "qalab-junit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "results.xml");
                JUnitWriter.WriteTo(path, results);
                var bytes = File.ReadAllBytes(path);
                Assert.AreNotEqual(0xEF, bytes[0], "no BOM");
                var text = Encoding.UTF8.GetString(bytes);
                StringAssert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<testsuites", text);
                StringAssert.DoesNotContain("\r", text);
                Assert.AreEqual("a <b> & \"c\"", Case(XDocument.Parse(text), "no_internal_errors").Element("error").Value);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
