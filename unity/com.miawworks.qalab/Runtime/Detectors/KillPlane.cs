using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// The height below which the player has left the world (spec 01: a setting, or the scene's lowest
    /// renderer − 5 m). Measured once per scene load: finding every renderer is too slow for every frame.
    /// </summary>
    internal static class KillPlane
    {
        public const float MarginM = 5f;

        /// <summary>
        /// <see cref="QALab.KillPlaneY"/> when the game set it; else the lowest renderer bound in the loaded
        /// scenes − 5 m; else negative infinity (nothing to fall off, e.g. a menu).
        /// </summary>
        public static float Measure()
        {
            if (QALab.KillPlaneY.HasValue) return QALab.KillPlaneY.Value;
#if UNITY_2023_1_OR_NEWER
            var renderers = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None);
#else
            var renderers = Object.FindObjectsOfType<Renderer>();
#endif
            var lowest = float.PositiveInfinity;
            foreach (var renderer in renderers)
            {
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer) continue;
                lowest = Mathf.Min(lowest, renderer.bounds.min.y);
            }
            return float.IsPositiveInfinity(lowest) ? float.NegativeInfinity : lowest - MarginM;
        }
    }
}
