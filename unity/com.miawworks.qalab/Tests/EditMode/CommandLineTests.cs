using System.Collections.Generic;
using MiawWorks.QALab;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    public class CommandLineTests
    {
        private static QALabOptions Parse(out List<string> errors, params string[] args)
        {
            errors = new List<string>();
            return CommandLine.Parse(args, errors);
        }

        [Test]
        public void DefaultsMatchTheSpec()
        {
            var o = Parse(out var errors, "QALabSandbox.exe", "-screen-width", "1280");
            Assert.IsEmpty(errors);
            Assert.IsFalse(o.Enabled);
            Assert.IsNull(o.OutDir);
            Assert.AreEqual(0, o.Seed);
            Assert.AreEqual(120f, o.DurationS);
            Assert.AreEqual("navmesh_explorer", o.Adapter);
            Assert.IsNull(o.Scene);
            Assert.AreEqual(5f, o.ShotEveryS);
            Assert.AreEqual("warning", o.MinLevel);
            Assert.IsTrue(o.Seeds.IsAll);
            Assert.IsFalse(o.Benchmark);
            Assert.IsFalse(o.QuitOnEnd);
        }

        [Test]
        public void ParsesEveryFlag()
        {
            var o = Parse(out var errors,
                "-qalab", "-qalabOut", "runs", "-qalabSeed", "42", "-qalabDuration", "60.5",
                "-qalabAdapter", "manual", "-qalabScene", "Sandbox_Menu", "-qalabShotEvery", "0",
                "-qalabMinLevel", "INFO", "-qalabSeeds", "sb01, SB06", "-qalabBenchmark", "-qalabQuitOnEnd");
            Assert.IsEmpty(errors, string.Join("; ", errors));
            Assert.IsTrue(o.Enabled);
            Assert.AreEqual("runs", o.OutDir);
            Assert.AreEqual(42, o.Seed);
            Assert.AreEqual(60.5f, o.DurationS);
            Assert.AreEqual("manual", o.Adapter);
            Assert.AreEqual("Sandbox_Menu", o.Scene);
            Assert.AreEqual(0f, o.ShotEveryS);
            Assert.AreEqual("info", o.MinLevel);
            CollectionAssert.AreEqual(new[] { "SB01", "SB06" }, o.Seeds.Ids);
            Assert.IsTrue(o.Seeds.IsEnabled("SB06"));
            Assert.IsFalse(o.Seeds.IsEnabled("SB02"));
            Assert.IsTrue(o.Benchmark);
            Assert.IsTrue(o.QuitOnEnd);
        }

        [Test]
        public void FlagNamesAreCaseInsensitive()
        {
            var o = Parse(out var errors, "-QALAB", "-QaLabSeed", "-7");
            Assert.IsEmpty(errors);
            Assert.IsTrue(o.Enabled);
            Assert.AreEqual(-7, o.Seed);
        }

        [TestCase("-qalabSeed", "abc")]
        [TestCase("-qalabSeed", "1.5")]
        [TestCase("-qalabDuration", "0")]
        [TestCase("-qalabDuration", "-3")]
        [TestCase("-qalabDuration", "NaN")]
        [TestCase("-qalabShotEvery", "-1")]
        [TestCase("-qalabAdapter", "NavMesh Explorer")]
        [TestCase("-qalabMinLevel", "verbose")]
        [TestCase("-qalabSeeds", "SB1")]
        [TestCase("-qalabSeeds", ",")]
        public void BadValuesAreReportedAndDefaultsKept(string flag, string value)
        {
            var defaults = new QALabOptions();
            var o = Parse(out var errors, "-qalab", flag, value);
            Assert.AreEqual(1, errors.Count, string.Join("; ", errors));
            StringAssert.StartsWith(flag, errors[0]);
            Assert.AreEqual(defaults.Seed, o.Seed);
            Assert.AreEqual(defaults.DurationS, o.DurationS);
            Assert.AreEqual(defaults.ShotEveryS, o.ShotEveryS);
            Assert.AreEqual(defaults.Adapter, o.Adapter);
            Assert.AreEqual(defaults.MinLevel, o.MinLevel);
            Assert.IsTrue(o.Seeds.IsAll);
            Assert.IsTrue(o.Enabled);
        }

        [Test]
        public void MissingValueAndUnknownFlagAreErrors()
        {
            var o = Parse(out var errors, "-qalabSeed", "-qalab", "-qalabOut", "-qalabFrobnicate");
            CollectionAssert.AreEqual(new[] { "-qalabSeed: missing value", "-qalabOut: missing value", "unknown flag -qalabFrobnicate" }, errors);
            Assert.IsTrue(o.Enabled);
            Assert.IsNull(o.OutDir);
        }

        [Test]
        public void SeedSelectionAllAndExplicit()
        {
            Assert.IsTrue(SeedSelection.Parse(" ALL ", out _).IsAll);
            var explicitIds = SeedSelection.Parse("SB14,sb02,SB14", out var error);
            Assert.IsNull(error);
            CollectionAssert.AreEqual(new[] { "SB02", "SB14" }, explicitIds.Ids);
            Assert.IsNull(SeedSelection.Parse("SB01,BAD", out error));
            StringAssert.Contains("BAD", error);
            Assert.IsEmpty(SeedSelection.All.Ids);
        }

        [Test]
        public void SeedsEnabledIsTheCatalogForAllAndTheGivenIdsForAList()
        {
            var catalog = new[] { "SB03", "SB01" };
            CollectionAssert.AreEqual(new[] { "SB01", "SB03" }, SeedSelection.All.EnabledIds(catalog));
            // SB02 has no catalog entry until its YOU WRITE task is done; SB06 arrives in M4. Both were
            // asked for, so both are recorded, the same as an id the catalog does know.
            CollectionAssert.AreEqual(new[] { "SB01", "SB02" }, SeedSelection.Parse("SB02,sb01", out _).EnabledIds(catalog));
            CollectionAssert.AreEqual(new[] { "SB06" }, SeedSelection.Parse("SB06", out _).EnabledIds(catalog));
        }
    }
}
