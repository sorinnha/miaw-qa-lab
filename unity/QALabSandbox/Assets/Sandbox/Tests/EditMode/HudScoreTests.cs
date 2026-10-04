using NUnit.Framework;

namespace QALab.Sandbox.Tests
{
    /// <summary>SB11: the seed keeps the long text; the fixed behaviour abbreviates from 10000 (design doc: "HUD").</summary>
    public class HudScoreTests
    {
        [TestCase(9999, false, "Score: 9999")]
        [TestCase(10000, false, "Score: 10000")]
        [TestCase(9999, true, "Score: 9999")]
        [TestCase(12345, true, "Score: 12.3K")]
        [TestCase(999999, true, "Score: 1000K")]
        [TestCase(1250000, true, "Score: 1.2M")]
        public void Format(int score, bool abbreviate, string expected)
        {
            Assert.AreEqual(expected, HudScore.Format(score, abbreviate));
        }
    }
}
