using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// Capsule player: CharacterController movement (WASD, 4.5 m/s, gravity −20 m/s²), footsteps every
    /// 0.6 m on the surface below, and respawn at the last checkpoint below the kill plane (y = −10),
    /// as "Player movement" in docs/sandbox_design.md says. Implements
    /// <see cref="IBotMover"/> so the M4 bot can drive it through the same code.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class SandboxPlayer : MonoBehaviour, IBotMover
    {
        [SerializeField, Min(0.1f)] private float moveSpeed = 4.5f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float killPlaneY = -10f;
        [SerializeField, Min(0.1f)] private float stepLengthM = 0.6f;
        [SerializeField] private Interactor interactor;
        [SerializeField] private FootstepAudio footsteps;

        private CharacterController _controller;
        private Vector3 _checkpoint;
        private float _verticalSpeed;
        private float _sinceStep;
        private Vector3? _botTarget;
        private string _forcedSurface;

        /// <summary>F1 menu: take one step on <paramref name="surface"/> next frame (normal OnStep path).</summary>
        public void RequestStep(string surface) => _forcedSurface = surface;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _checkpoint = transform.position;   // the start is the first checkpoint
        }

        private void Start() => SandboxSeeds.RegisterPlayer(transform, this);

        private void Update()
        {
            var move = DesiredDirection() * moveSpeed;
            _verticalSpeed = _controller.isGrounded ? -1f : _verticalSpeed + gravity * Time.deltaTime;
            move.y = _verticalSpeed;
            var before = transform.position;
            _controller.Move(move * Time.deltaTime);

            var walked = Vector3.ProjectOnPlane(transform.position - before, Vector3.up).magnitude;
            if (_controller.isGrounded && walked > 0f)
            {
                _sinceStep += walked;
                if (_sinceStep >= stepLengthM)
                {
                    _sinceStep = 0f;
                    OnStep();
                }
            }
            if (_forcedSurface != null)
            {
                OnStep();
            }
            if (transform.position.y < killPlaneY)
            {
                Respawn();
            }
        }

        /// <summary>Keyboard input, or the bot's target when one is set.</summary>
        private Vector3 DesiredDirection()
        {
            if (_botTarget.HasValue)
            {
                var flat = Vector3.ProjectOnPlane(_botTarget.Value - transform.position, Vector3.up);
                return flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.zero;
            }
            var input = SandboxInput.Move;
            return new Vector3(input.x, 0f, input.y);
        }

        private void OnStep()
        {
            var surface = _forcedSurface ?? SurfaceUnderFoot();
            _forcedSurface = null;
            if (footsteps != null)
            {
                footsteps.Play(surface);
            }
        }

        private string SurfaceUnderFoot()
        {
            if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out var hit, 3f))
            {
                var tag = hit.collider.GetComponent<SurfaceTag>();
                if (tag != null) return tag.Surface;
            }
            return "Grass";
        }

        /// <summary>Respawns happen here from now on (checkpoint zones call this).</summary>
        public void SetCheckpoint(Vector3 position) => _checkpoint = position;

        // ---- IBotMover (driven by the M4 bot) -------------------------------------------------------

        public void MoveTowards(Vector3 worldTarget) => _botTarget = worldTarget;

        public void Stop() => _botTarget = null;

        /// <summary>Queues the nearest door; it opens next frame through the same path as the E key.</summary>
        public bool TryInteract(out string objectName)
        {
            objectName = null;
            return interactor != null && interactor.RequestInteractNearest(out objectName);
        }

        public void Respawn()
        {
            // A CharacterController overrides transform changes while enabled, so turn it off to teleport.
            _controller.enabled = false;
            transform.position = _checkpoint;
            _controller.enabled = true;
            _verticalSpeed = 0f;
            _botTarget = null;
        }
    }
}
