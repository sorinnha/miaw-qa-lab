using System.Threading;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Main-thread facts (scene, frame, player position) that any thread may read.
    /// Unity APIs like <c>Time.frameCount</c> or <c>transform.position</c> may only be called on the main
    /// thread, but log callbacks arrive on any thread. So the host copies them into volatile fields
    /// once per frame (and on scene change), and other threads read only these copies.
    /// </summary>
    public sealed class MainThreadCache : IMainThreadState
    {
        private volatile string _scene;
        private long _frame;
        // Three floats instead of a Vector3: a struct can't be volatile. A reader may see x from this
        // frame and z from the last one; for log context that's fine, and it avoids allocating.
        private volatile float _x, _y, _z;
        private volatile bool _hasPosition;

        public string Scene => _scene;

        public long Frame => Interlocked.Read(ref _frame);

        /// <summary>A fresh array per call (each event owns its copy), or null without a player.</summary>
        public float[] Position => _hasPosition ? new[] { Round(_x), Round(_y), Round(_z) } : null;

        /// <summary>Main thread only: copy this frame's values.</summary>
        public void Capture(string scene, int frame, Transform player)
        {
            _scene = NullIfEmpty(scene);
            Interlocked.Exchange(ref _frame, frame);
            if (player != null)
            {
                var p = player.position;
                _x = p.x;
                _y = p.y;
                _z = p.z;
                _hasPosition = true;
            }
            else
            {
                _hasPosition = false;
            }
        }

        public void SetScene(string scene) => _scene = NullIfEmpty(scene);

        // Before the first scene finishes loading the active scene has no name; write null, not "".
        private static string NullIfEmpty(string scene) => string.IsNullOrEmpty(scene) ? null : scene;

        // 2 decimals (1 cm) keeps lines short: 7.6 instead of 7.59999847.
        private static float Round(float v) => Mathf.Round(v * 100f) / 100f;
    }
}
