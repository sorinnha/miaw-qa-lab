using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// The ballistics range (design doc): while the player is within 15 m, fires a 120 m/s projectile east
    /// at a 5 cm wall every 1.5 s. SB16: projectiles use <c>CollisionDetectionMode.Discrete</c>, so they
    /// pass through the wall (a 120 m/s body moves 2.4 m per 50 Hz physics step and is never checked
    /// inside the wall). QA Lab's <see cref="TunnelingDetector"/>, added to every projectile, should report
    /// <c>tunneling</c>. With the seed off they use ContinuousDynamic and stop at the wall.
    /// </summary>
    public sealed class ProjectileLauncher : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float speedMps = 120f;
        [SerializeField, Min(0.1f)] private float intervalS = 1.5f;
        [SerializeField, Min(1f)] private float activeRadiusM = SandboxLayout.RangeActiveRadiusM;
        [SerializeField, Min(0.05f)] private float lifetimeS = 0.5f;
        [SerializeField, Min(1)] private int magazine = 30;

        private float _next;
        private bool _requested;

        /// <summary>True while the player is close enough for the range to fire (the ammo HUD shows then).</summary>
        public bool IsActive { get; private set; }

        public int Ammo { get; private set; }

        /// <summary>F1 menu: fire one projectile on the next frame.</summary>
        public void RequestShot() => _requested = true;

        private void Awake() => Ammo = magazine;

        private void Update()
        {
            var player = SandboxSeeds.Player;
            IsActive = player != null && Vector3.Distance(player.position, transform.position) <= activeRadiusM;
            if (!_requested && !(IsActive && Time.time >= _next)) return;
            _requested = false;
            _next = Time.time + intervalS;
            Fire();
        }

        private void Fire()
        {
            Ammo = Ammo > 1 ? Ammo - 1 : magazine;
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile";
            go.transform.localScale = Vector3.one * 0.1f;
            go.transform.position = transform.position + transform.forward * 0.5f;
            var body = go.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.collisionDetectionMode = SandboxSeeds.IsEnabled("SB16")
                ? CollisionDetectionMode.Discrete
                : CollisionDetectionMode.ContinuousDynamic;
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = transform.forward * speedMps;
#else
            body.velocity = transform.forward * speedMps;
#endif
            go.AddComponent<Projectile>().Launch(SandboxLayout.ThinWallX, lifetimeS);
            go.AddComponent<TunnelingDetector>();
        }
    }
}
