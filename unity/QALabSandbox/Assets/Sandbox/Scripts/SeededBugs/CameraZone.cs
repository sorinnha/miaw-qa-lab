using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB10 (Camera): entering the camera trigger zone (x 2–8, z 14–20) turns the 3D view off for 2 s: the
    /// camera's culling mask becomes 0 and it clears to black. The Screen Space - Overlay HUD still draws
    /// on top, so the frame is black with the HUD, which vision should label <c>black_screen</c>. The
    /// design doc allows zones to change framing, never to stop the 3D view. With the seed off the zone
    /// does nothing.
    /// </summary>
    public sealed class CameraZone : MonoBehaviour, IVisualSeed
    {
        [SerializeField, Min(0.1f)] private float blackoutS = 2f;

        private Camera _camera;
        private int _savedMask;
        private CameraClearFlags _savedFlags;
        private Color _savedBackground;
        private float _until;
        private bool _black;
        private bool _inside;
        private bool _requested;

        public string BugId => "SB10";

        public string Label => VisualLabels.BlackScreen;

        /// <summary>True while the 3D view is off.</summary>
        public bool IsBlack => _black;

        /// <summary>F1 menu: black out now, wherever the player is.</summary>
        public void RequestBlackout() => _requested = true;

        private void OnEnable() => VisualLabelProbe.Register(this);

        private void OnDisable()
        {
            VisualLabelProbe.Unregister(this);
            if (_black) Restore();
        }

        private void Update()
        {
            var player = SandboxSeeds.Player;
            var inside = player != null && SandboxLayout.Inside(player.position.x, player.position.z,
                SandboxLayout.CameraZoneX0, SandboxLayout.CameraZoneX1, SandboxLayout.CameraZoneZ0, SandboxLayout.CameraZoneZ1);
            var entered = inside && !_inside;
            _inside = inside;
            if ((entered || _requested) && !_black && SandboxSeeds.IsEnabled(BugId))
            {
                BlackOut();
            }
            _requested = false;
            if (_black && Time.time >= _until)
            {
                Restore();
            }
        }

        public bool IsVisible(Camera camera) => _black;

        private void BlackOut()
        {
            _camera = Camera.main;
            if (_camera == null) return;
            _savedMask = _camera.cullingMask;
            _savedFlags = _camera.clearFlags;
            _savedBackground = _camera.backgroundColor;
            _camera.cullingMask = 0;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = Color.black;
            _black = true;
            _until = Time.time + blackoutS;
        }

        private void Restore()
        {
            _black = false;
            if (_camera == null) return;
            _camera.cullingMask = _savedMask;
            _camera.clearFlags = _savedFlags;
            _camera.backgroundColor = _savedBackground;
        }
    }
}
