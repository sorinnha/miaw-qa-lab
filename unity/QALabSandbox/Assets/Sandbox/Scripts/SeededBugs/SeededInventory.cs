using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB02 (Inventory): exactly 5 slots (0–4). The HUD sometimes asks for a stale index from a bigger
    /// bag, and GetSlot reports it. See "Inventory" in docs/sandbox_design.md.
    /// </summary>
    public sealed class SeededInventory : MonoBehaviour
    {
        public const int Size = 5;

        // Field initializer (not Awake) so EditMode tests see the items without entering Play Mode.
        private readonly string[] _slots = { "Medkit", "Ammo", null, "Key", null };

        /// <summary>
        /// YOU WRITE (M1, about 10 lines): return the item name in slot <paramref name="index"/>, or null
        /// for an empty slot.
        /// <list type="bullet">
        /// <item>Valid indices are 0 to <see cref="Size"/> − 1.</item>
        /// <item>For any other index: call <c>LabelRecorder.Trigger("SB02")</c> first, then log with
        /// <c>Debug.LogError</c> exactly <c>Inventory slot {index} out of range (size 5)</c> (use
        /// <see cref="Size"/>, not a literal 5), and return null. Never throw: the HUD keeps working.</item>
        /// <item>Why Debug.LogError and not an exception? The HUD has to keep working after a bad index,
        /// and the sample run (the triage fixture) records SB02 as an error log, not an exception.</item>
        /// </list>
        /// Tests: <c>Tests/EditMode/SeededInventoryTests.cs</c> (category YouWrite).
        /// </summary>
        public string GetSlot(int index)
        {
            throw new System.NotImplementedException("YOU WRITE");
        }
    }
}
