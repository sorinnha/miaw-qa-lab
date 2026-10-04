using System.Globalization;
using MiawWorks.QALab;
using UnityEngine;
using UnityEngine.UI;

namespace QALab.Sandbox
{
    /// <summary>
    /// The score label, top-left (design doc: "HUD"). The score grows with play time. SB11: the label sits
    /// in a fixed-width box and never abbreviates, so from 10000 points the text runs out of its box,
    /// which vision should label <c>ui_overflow</c>. With the seed off, values from 10000 are shortened
    /// (12.3K) as the design doc asks.
    /// </summary>
    public sealed class HudScore : MonoBehaviour, IVisualSeed
    {
        [SerializeField] private Text label;
        [SerializeField, Min(0f)] private float pointsPerSecond = 125f;

        private float _points;
        private int _shown = -1;

        public string BugId => "SB11";

        public string Label => VisualLabels.UiOverflow;

        public int Score => (int)_points;

        /// <summary>F1 menu: add points (10000 makes SB11 visible at once).</summary>
        public void AddPoints(int points) => _points += points;

        /// <summary>True when the text is wider than its box.</summary>
        public bool IsOverflowing => label != null && label.preferredWidth > label.rectTransform.rect.width + 0.5f;

        private void OnEnable() => VisualLabelProbe.Register(this);

        private void OnDisable() => VisualLabelProbe.Unregister(this);

        private void Update()
        {
            _points += pointsPerSecond * Time.deltaTime;
            if (Score == _shown || label == null) return;
            _shown = Score;
            label.text = Format(_shown, abbreviate: !SandboxSeeds.IsEnabled(BugId));
        }

        public bool IsVisible(Camera camera) =>
            SandboxSeeds.IsEnabled(BugId) && IsOverflowing && VisualLabelProbe.IsOnScreen(label.rectTransform);

        /// <summary><c>Score: 9999</c>; with <paramref name="abbreviate"/>, <c>Score: 12.3K</c> from 10000.</summary>
        public static string Format(int score, bool abbreviate)
        {
            if (!abbreviate || score < 10000) return "Score: " + score.ToString(CultureInfo.InvariantCulture);
            var value = score >= 1000000 ? (score / 1000000f).ToString("0.#", CultureInfo.InvariantCulture) + "M"
                : (score / 1000f).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return "Score: " + value;
        }
    }
}
