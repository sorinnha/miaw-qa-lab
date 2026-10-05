using System.Globalization;
using MiawWorks.QALab;
using UnityEngine;
using UnityEngine.UI;

namespace QALab.Sandbox
{
    /// <summary>
    /// The ammo icon and count, bottom-right (design doc: "HUD"), shown while the ballistics range is
    /// active. SB12: the icon Image loses its sprite at runtime, so Unity draws a plain white box, which
    /// vision should label <c>placeholder_ui</c>. The design doc says every HUD image must have a sprite.
    /// </summary>
    public sealed class HudAmmo : MonoBehaviour, IVisualSeed
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Image icon;
        [SerializeField] private Text count;
        [SerializeField] private ProjectileLauncher launcher;

        private int _shown = -1;

        public string BugId => "SB12";

        public string Label => VisualLabels.PlaceholderUi;

        private void Awake()
        {
            if (SandboxSeeds.IsEnabled(BugId) && icon != null)
            {
                icon.sprite = null;
            }
        }

        private void OnEnable() => VisualLabelProbe.Register(this);

        private void OnDisable() => VisualLabelProbe.Unregister(this);

        private void Update()
        {
            var active = launcher != null && launcher.IsActive;
            if (panel != null && panel.activeSelf != active) panel.SetActive(active);
            if (!active || count == null || launcher.Ammo == _shown) return;
            _shown = launcher.Ammo;
            count.text = _shown.ToString(CultureInfo.InvariantCulture);
        }

        public bool IsVisible(Camera camera) =>
            icon != null && icon.sprite == null && panel != null && panel.activeInHierarchy
            && VisualLabelProbe.IsOnScreen(icon.rectTransform);
    }
}
