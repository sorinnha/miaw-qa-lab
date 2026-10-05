using System;
using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB08 (Performance): while the player is inside GcZone (x 16–24, z 16–24), it creates about 50 MB of
    /// short-lived objects on entry and every 8 s, the kind of per-frame garbage the design doc forbids.
    /// The collection that follows stalls the frame, and QA Lab's perf detector should report
    /// <c>perf_spike</c>. (The garbage is collected right away so the stall lands in the same frame and the
    /// test is repeatable; a real game would pay it at a random later frame.)
    /// </summary>
    public sealed class GcZone : MonoBehaviour
    {
        [SerializeField, Min(1)] private int megabytes = 50;
        [SerializeField, Min(16)] private int objectBytes = 128;
        [SerializeField, Min(1f)] private float intervalS = 8f;

        private bool _inside;
        private float _next;
        private bool _requested;

        /// <summary>F1 menu: one burst on the next frame, wherever the player is.</summary>
        public void RequestBurst() => _requested = true;

        private void Update()
        {
            var player = SandboxSeeds.Player;
            var inside = player != null && SandboxLayout.Inside(player.position.x, player.position.z,
                SandboxLayout.GcZoneX0, SandboxLayout.GcZoneX1, SandboxLayout.GcZoneZ0, SandboxLayout.GcZoneZ1);
            if (inside && !_inside) _next = Time.time;   // a burst on entry
            _inside = inside;
            if (!_requested && !(inside && Time.time >= _next)) return;
            _requested = false;
            _next = Time.time + intervalS;
            if (SandboxSeeds.IsEnabled("SB08"))
            {
                AllocateGarbage();
            }
        }

        private void AllocateGarbage()
        {
            LabelRecorder.Trigger("SB08");
            var count = (int)Math.Min(int.MaxValue, megabytes * 1024L * 1024L / objectBytes);
            var garbage = new byte[count][];
            for (var i = 0; i < garbage.Length; i++)
            {
                garbage[i] = new byte[objectBytes];
            }
            GC.Collect();   // everything is still reachable here, so the collector walks all of it
            GC.KeepAlive(garbage);
        }
    }
}
