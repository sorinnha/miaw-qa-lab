#if QALAB_PHYSICS
using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// <c>tunneling</c> (spec 01, optional, major). Add it to a fast projectile prefab. Every physics step it
    /// casts a line from where the body was to where it is now; if the line crosses a collider and the
    /// body got no collision callback in between, the body passed through that collider.
    /// <para>
    /// Why it happens: with <c>CollisionDetectionMode.Discrete</c> physics only checks overlaps at the
    /// positions of each step. A 120 m/s projectile at the default 50 Hz moves 2.4 m per step, so a 5 cm
    /// wall sits between two checked positions and is never touched. Fixes:
    /// <c>CollisionDetectionMode.ContinuousDynamic</c> (or ContinuousSpeculative), thicker colliders, a swept
    /// raycast in the projectile's own code, or a lower speed / smaller fixed timestep.
    /// </para>
    /// Reports go through <see cref="QALab.ReportDetector(string, string, JObject)"/>; outside a run this
    /// component does nothing but the line cast.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class TunnelingDetector : MonoBehaviour
    {
        [Tooltip("Colliders that count as walls.")]
        [SerializeField] private LayerMask layers = Physics.DefaultRaycastLayers;
        [Tooltip("Slower bodies are ignored (they can't skip a collider).")]
        [SerializeField, Min(0f)] private float minSpeedMps = 20f;

        private Rigidbody _body;
        private Vector3 _previous;
        private bool _hasPrevious;
        private bool _collided;

        private void Awake() => _body = GetComponent<Rigidbody>();

        private void OnEnable() => _hasPrevious = false;

        // FixedUpdate runs before each physics step, and collision callbacks run after the step. So here
        // _previous → position is the move of the step that just finished, and _collided says whether
        // that step reported a collision.
        private void FixedUpdate()
        {
            var position = _body.position;
            if (_hasPrevious && !_collided && QALab.IsRunning)
            {
                var move = position - _previous;
                var speed = move.magnitude / Time.fixedDeltaTime;
                if (speed >= minSpeedMps
                    && Physics.Linecast(_previous, position, out var hit, layers, QueryTriggerInteraction.Ignore)
                    && !hit.collider.transform.IsChildOf(transform))
                {
                    // Reported at the wall, not at the player: the 4 m cell (and triage's clusters) follow the bug.
                    QALab.ReportDetector(DetectorNames.Tunneling, DetectorSeverity.Major, new JObject
                    {
                        ["object"] = name,
                        ["crossed"] = hit.collider.name,
                        ["speed_mps"] = Math.Round(speed, 1),
                        ["step_m"] = Math.Round(move.magnitude, 2),
                        ["fixed_dt_s"] = Math.Round(Time.fixedDeltaTime, 4),
                        ["collision_mode"] = _body.collisionDetectionMode.ToString(),
                        ["player_pos"] = QALab.Player != null ? PosArray(QALab.Player.position) : null,
                    }, hit.point);
                }
            }
            _previous = position;
            _hasPrevious = true;
            _collided = false;
        }

        private void OnCollisionEnter(Collision collision) => _collided = true;

        private static JArray PosArray(Vector3 p) =>
            new JArray((float)Math.Round(p.x, 2), (float)Math.Round(p.y, 2), (float)Math.Round(p.z, 2));
    }
}
#endif
