using System;
using System.Collections.Generic;
using System.IO;
using MiawWorks.QALab;
using Newtonsoft.Json;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    public class RunInfoTests
    {
        private static string SampleRunJson => RepoPaths.ReadText("samples", "sample_run", "run.json");

        [Test]
        public void SampleRunJsonRoundTrips()
        {
            Json.AssertSame(SampleRunJson, RunInfo.FromJson(SampleRunJson).ToJson());
        }

        [Test]
        public void BuiltFromValuesMatchesTheSample()
        {
            var run = new RunInfo
            {
                RunId = RunIds.Create(new DateTime(2026, 10, 5, 10, 30, 0, DateTimeKind.Utc), 42),
                Project = "QALabSandbox",
                QALabVersion = "0.1.0",
                Build = new BuildInfo { Version = "0.1.0", Platform = "StandaloneWindows64", Unity = "6000.0.0f1", GitSha = "0000000", Development = true },
                Mode = "player",
                Adapter = "navmesh_explorer",
                Seed = 42,
                Scenes = new List<string> { "Sandbox_Level01" },
                StartedAt = Timestamps.Iso(new DateTime(2026, 10, 5, 10, 30, 0, DateTimeKind.Utc)),
                Machine = new MachineInfo { Os = "Windows 11", Cpu = "example CPU", Gpu = "example GPU", RamGb = 16 },
                Benchmark = true,
                SeedsEnabled = new List<string> { "SB01", "SB02", "SB03", "SB04", "SB06", "SB08", "SB09", "SB10", "SB13", "SB14" },
            };
            run.MarkEnded(new DateTime(2026, 10, 5, 10, 30, 55, 100, DateTimeKind.Utc), 55.0, ExitReasons.DurationElapsed, 1);
            Assert.AreEqual("20261005T103000Z-s42", run.RunId);
            Json.AssertSame(SampleRunJson, run.ToJson());
        }

        [Test]
        public void StartOfRunHasExplicitNullEnd()
        {
            var run = new RunInfo { RunId = "r", Project = "p", Mode = "editor_playmode", StartedAt = "2026-10-05T10:30:00.000Z" };
            var json = Json.Parse(run.ToJson());
            Assert.AreEqual(Newtonsoft.Json.Linq.JTokenType.Null, json["ended_at"].Type);
            Assert.AreEqual(Newtonsoft.Json.Linq.JTokenType.Null, json["exit_reason"].Type);
            Assert.IsNull(json["duration_s"], "duration_s is written only at the end");
        }

        [Test]
        public void UnknownFieldIsRejectedWhenReading()
        {
            Assert.Throws<JsonSerializationException>(() => RunInfo.FromJson("{\"schema\":\"qalab.run/1\",\"surprise\":1}"));
        }

        [Test]
        public void RunIdsAreWindowsSafe()
        {
            Assert.AreEqual("20261231T235959Z-s-3", RunIds.Create(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc), -3));
        }

        [Test]
        public void WriteToReplacesTheFileWithLfAndNoBom()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qalab-run-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "run.json");
                var run = new RunInfo { RunId = "r", Project = "p", Mode = "player", StartedAt = "2026-10-05T10:30:00.000Z" };
                run.WriteTo(path);
                run.MarkEnded(new DateTime(2026, 10, 5, 10, 31, 0, DateTimeKind.Utc), 60, ExitReasons.UserQuit, 0);
                run.WriteTo(path);
                var bytes = File.ReadAllBytes(path);
                Assert.AreNotEqual(0xEF, bytes[0]);
                CollectionAssert.DoesNotContain(bytes, (byte)'\r');
                var json = Json.Parse(File.ReadAllText(path));
                Assert.AreEqual("2026-10-05T10:31:00.000Z", (string)json["ended_at"]);
                Assert.AreEqual("user_quit", (string)json["exit_reason"]);
                Assert.IsFalse(File.Exists(path + ".tmp"));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    /// <summary>The build stamp BuildRunner writes and run.json → build.git_sha reads.</summary>
    public class BuildStampTests
    {
        [Test]
        public void StampRoundTripsAndRejectsJunk()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qalab-stamp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                Assert.IsNull(BuildStamp.ReadGitSha(dir), "no file: unknown, not an error");
                var path = Path.Combine(dir, BuildStamp.FileName);
                File.WriteAllText(path, BuildStamp.ToJson("4bd741f0c2a9"));
                Assert.AreEqual("4bd741f0c2a9", BuildStamp.ReadGitSha(dir));
                File.WriteAllText(path, BuildStamp.ToJson("not a sha"));
                Assert.IsNull(BuildStamp.ReadGitSha(dir));
                File.WriteAllText(path, "{ broken");
                Assert.IsNull(BuildStamp.ReadGitSha(dir), "a broken stamp never stops a run");
                Assert.IsNull(BuildStamp.ReadGitSha(null));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [TestCase("4bd741f", true)]
        [TestCase("4bd741f0c2a94bd741f0c2a94bd741f0c2a94bd7", true)]
        [TestCase("4BD741F", false)]
        [TestCase("4bd74", false)]
        [TestCase(null, false)]
        public void ShaIsSevenToFortyLowercaseHexDigits(string value, bool expected)
        {
            Assert.AreEqual(expected, BuildStamp.IsSha(value));
        }
    }
}
