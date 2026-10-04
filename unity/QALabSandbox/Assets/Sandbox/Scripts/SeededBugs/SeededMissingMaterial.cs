using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB09 (Art assets): Crate_07 loses its material at runtime, so Unity draws it bright magenta, which
    /// the design doc says must never appear. Vision should label shots that show it
    /// <c>missing_texture</c>; this seed tells labels.json which shots do (in view, ≥ 1% of the screen,
    /// not hidden behind a wall).
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class SeededMissingMaterial : MonoBehaviour, IVisualSeed
    {
        private Renderer _renderer;

        public string BugId => "SB09";

        public string Label => VisualLabels.MissingTexture;

        private void Awake()
        {
            _renderer = GetComponent<Renderer>();
            if (SandboxSeeds.IsEnabled(BugId))
            {
                _renderer.sharedMaterial = null;
            }
        }

        private void OnEnable() => VisualLabelProbe.Register(this);

        private void OnDisable() => VisualLabelProbe.Unregister(this);

        public bool IsVisible(Camera camera) =>
            _renderer.sharedMaterial == null && VisualLabelProbe.IsOnScreen(_renderer, camera);
    }
}
