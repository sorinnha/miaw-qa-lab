// Unity-only (uses UnityEngine): runs in Unity's Test Runner, not in tools/cs-check.
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace QALab.Sandbox.Tests
{
    /// <summary>SB02: SeededInventory.GetSlot (a learning task, written by Claude at Sora's request, D-030).</summary>
    public class SeededInventoryTests
    {
        private GameObject _go;
        private SeededInventory _inventory;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("inventory");
            _inventory = _go.AddComponent<SeededInventory>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        [Test]
        public void ValidSlotsReturnTheirItemOrNull()
        {
            Assert.AreEqual("Medkit", _inventory.GetSlot(0));
            Assert.AreEqual("Ammo", _inventory.GetSlot(1));
            Assert.IsNull(_inventory.GetSlot(2));
            Assert.AreEqual("Key", _inventory.GetSlot(3));
            Assert.IsNull(_inventory.GetSlot(4));
            LogAssert.NoUnexpectedReceived();
        }

        [TestCase(7)]
        [TestCase(9)]
        [TestCase(5)]
        [TestCase(-1)]
        public void OutOfRangeLogsTheExactErrorAndReturnsNull(int index)
        {
            LogAssert.Expect(LogType.Error, $"Inventory slot {index} out of range (size 5)");
            Assert.IsNull(_inventory.GetSlot(index));
        }

        [Test]
        public void SizeIsFive()
        {
            Assert.AreEqual(5, SeededInventory.Size);
        }
    }
}
