using MiawWorks.QALab;
using NUnit.Framework;

namespace MiawWorks.QALab.Tests
{
    public class LogLevelsTests
    {
        [TestCase("info", "warning", false)]
        [TestCase("warning", "warning", true)]
        [TestCase("error", "warning", true)]
        [TestCase("exception", "error", true)]
        [TestCase("assert", "exception", true)]
        [TestCase("info", "info", true)]
        [TestCase("error", "exception", false)]
        [TestCase("bogus", "info", false)]
        [TestCase("warning", "bogus", true)]   // unknown minimum falls back to warning
        [TestCase("info", "bogus", false)]
        public void Passes(string level, string min, bool expected)
        {
            Assert.AreEqual(expected, LogLevels.Passes(level, min));
        }

        [Test]
        public void OwnMessagesAreRecognised()
        {
            Assert.IsTrue(LogLevels.IsOwnMessage("[QALab] run folder: runs/x"));
            Assert.IsFalse(LogLevels.IsOwnMessage("Door [QALab] mention"));
            Assert.IsFalse(LogLevels.IsOwnMessage(null));
        }

        [Test]
        public void KnownLevelsAreTheSchemaEnum()
        {
            foreach (var level in new[] { "info", "warning", "error", "exception", "assert" })
            {
                Assert.IsTrue(LogLevels.IsKnown(level), level);
            }
            Assert.IsFalse(LogLevels.IsKnown("log"));
        }
    }
}
