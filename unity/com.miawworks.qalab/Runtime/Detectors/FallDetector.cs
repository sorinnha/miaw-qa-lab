// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using Newtonsoft.Json.Linq;

namespace MiawWorks.QALab
{
    /// <summary>
    /// <c>fell_out_of_world</c> (spec 01): the player is below the kill plane, so it fell through the floor
    /// or off the level. Reports once per fall (critical), then asks the game to respawn the player
    /// through <c>IBotMover.Respawn</c> so the playtest can go on. The kill plane comes from the host
    /// (<c>QALab.KillPlaneY</c> if the game sets it, else the lowest renderer in the scene − 5 m).
    /// It must sit above the game's own respawn height, or the game teleports the player before QA Lab
    /// sees the fall.
    /// </summary>
    public sealed class FallDetector : IDetector
    {
        /// <summary>A frame whose vertical speed is below this counts as standing on something.</summary>
        public const float GroundedSpeedMps = 0.5f;

        private readonly Func<float> _killPlaneY;
        private readonly Action _respawn;
        private bool _below;
        // Plain floats, not arrays: Tick runs every frame and must not allocate.
        private bool _hasGrounded, _hasPrevious;
        private float _groundX, _groundY, _groundZ;
        private float _previousY;
        private double _previousT;

        /// <param name="killPlaneY">Current kill plane height (read every frame; it changes per scene).</param>
        /// <param name="respawn">Puts the player back; null when no mover is registered.</param>
        public FallDetector(Func<float> killPlaneY, Action respawn)
        {
            _killPlaneY = killPlaneY ?? throw new ArgumentNullException(nameof(killPlaneY));
            _respawn = respawn;
        }

        public string Name => DetectorNames.FellOutOfWorld;

        public void Tick(in DetectorFrame frame, IDetectorReporter reporter)
        {
            var pos = frame.PlayerPos;
            if (pos == null)
            {
                _hasPrevious = false;
                return;
            }
            TrackGround(pos, frame.T);
            var killPlane = _killPlaneY();
            if (pos[1] >= killPlane)
            {
                _below = false;
                return;
            }
            if (_below)
            {
                return;   // already reported this fall; the respawn hasn't taken effect yet
            }
            _below = true;
            var details = new JObject { ["kill_plane_y"] = Math.Round(killPlane, 2) };
            if (_hasGrounded)
            {
                details["last_grounded_pos"] = new JArray(Round(_groundX), Round(_groundY), Round(_groundZ));
            }
            reporter.Report(Name, DetectorSeverity.Critical, details);
            _respawn?.Invoke();
            _hasPrevious = false;   // the respawn teleports: don't read the jump as vertical speed
        }

        // Remember where the player last stood still vertically: that's roughly where it fell from.
        private void TrackGround(float[] pos, double t)
        {
            if (_hasPrevious && t > _previousT)
            {
                var verticalSpeed = Math.Abs(pos[1] - _previousY) / (t - _previousT);
                if (verticalSpeed < GroundedSpeedMps)
                {
                    _hasGrounded = true;
                    _groundX = pos[0];
                    _groundY = pos[1];
                    _groundZ = pos[2];
                }
            }
            _hasPrevious = true;
            _previousY = pos[1];
            _previousT = t;
        }

        private static float Round(float v) => (float)Math.Round(v, 2);
    }
}
