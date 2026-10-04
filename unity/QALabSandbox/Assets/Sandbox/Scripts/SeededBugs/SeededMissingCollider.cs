using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB06 (Level geometry): tile T_17 keeps its renderer but loses its collider, so the floor looks solid
    /// and the NavMesh (baked from render meshes) says it is walkable, but the player drops through it.
    /// QA Lab's fall detector should report <c>fell_out_of_world</c> near T_17. The tile records the seed
    /// when the player sinks through it (ground truth for labels.json).
    /// </summary>
    [RequireComponent(typeof(Collider), typeof(Renderer))]
    public sealed class SeededMissingCollider : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float fallDepthM = 1f;

        private Collider _collider;
        private Bounds _bounds;
        private bool _falling;

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _bounds = GetComponent<Renderer>().bounds;
            if (SandboxSeeds.IsEnabled("SB06"))
            {
                _collider.enabled = false;
            }
        }

        private void Update()
        {
            if (_collider.enabled) return;
            var player = SandboxSeeds.Player;
            if (player == null) return;
            var p = player.position;
            var over = p.x >= _bounds.min.x && p.x <= _bounds.max.x && p.z >= _bounds.min.z && p.z <= _bounds.max.z;
            if (over && p.y < _bounds.max.y - fallDepthM)
            {
                if (_falling) return;
                _falling = true;
                LabelRecorder.Trigger("SB06");
            }
            else if (!over || p.y >= _bounds.max.y)
            {
                _falling = false;   // respawned or walked off: the next fall counts again
            }
        }
    }
}
