using UnityEngine;
using UnityEngine.UI;

namespace QALab.Sandbox
{
    /// <summary>
    /// The inventory HUD: Tab toggles it, and opening refreshes all 5 slots plus the highlighted one.
    /// The bug behind SB02 lives here: the highlight index is left over from a bigger bag (7, then 9),
    /// so <see cref="SeededInventory.GetSlot"/> gets an index outside 0–4.
    /// </summary>
    public sealed class HudInventory : MonoBehaviour
    {
        private static readonly int[] StaleHighlights = { 7, 9 };

        [SerializeField] private SeededInventory inventory;
        [SerializeField] private GameObject panel;
        [SerializeField] private Image[] slotImages = new Image[0];
        [SerializeField] private Color filled = new Color(0.9f, 0.8f, 0.3f);
        [SerializeField] private Color empty = new Color(0.2f, 0.2f, 0.2f, 0.6f);

        private int _opens;
        private bool _openRequested;

        /// <summary>F1 menu: open the inventory on the next frame (normal Update path).</summary>
        public void RequestOpen() => _openRequested = true;

        private void Start()
        {
            if (panel != null) panel.SetActive(false);
        }

        private void Update()
        {
            if (_openRequested || SandboxInput.InventoryPressed)
            {
                var force = _openRequested;
                _openRequested = false;
                if (force || panel == null || !panel.activeSelf) Open();
                else panel.SetActive(false);
            }
        }

        public void Open()
        {
            if (panel != null) panel.SetActive(true);
            Refresh();
            _opens++;
        }

        private void Refresh()
        {
            for (var i = 0; i < slotImages.Length && i < SeededInventory.Size; i++)
            {
                slotImages[i].color = inventory.GetSlot(i) != null ? filled : empty;
            }
            var highlight = SandboxSeeds.IsEnabled("SB02")
                ? StaleHighlights[_opens % StaleHighlights.Length]   // SB02: stale index from a bigger bag
                : Mathf.Clamp(_opens, 0, SeededInventory.Size - 1);
            inventory.GetSlot(highlight);
        }
    }
}
