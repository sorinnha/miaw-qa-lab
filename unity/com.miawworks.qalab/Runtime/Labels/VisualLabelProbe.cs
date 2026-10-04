using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Asks the registered <see cref="IVisualSeed"/>s what is visible before each screenshot (benchmark mode
    /// only) and offers the visibility tests the spec describes, so every seed answers the same way:
    /// a renderer counts when it is inside the camera frustum, its projected bounds cover at least 1% of
    /// the screen and nothing blocks the line of sight to its centre; a UI element counts when it is
    /// active and its rectangle is on screen. These are heuristics: a renderer half hidden behind a wall
    /// whose centre is visible still counts. Main thread only.
    /// </summary>
    public static class VisualLabelProbe
    {
        public const float DefaultMinCoverage = 0.01f;

        private static readonly List<IVisualSeed> Seeds = new List<IVisualSeed>();
        private static readonly Plane[] Planes = new Plane[6];
        private static readonly Vector3[] Corners = new Vector3[8];
        private static readonly Vector3[] UiCorners = new Vector3[4];

        public static void Register(IVisualSeed seed)
        {
            if (seed != null && !Seeds.Contains(seed)) Seeds.Add(seed);
        }

        public static void Unregister(IVisualSeed seed) => Seeds.Remove(seed);

        /// <summary>Labels and seed ids visible right now, from enabled seeds only. Clears both lists first.</summary>
        internal static void Probe(Camera camera, List<string> labels, List<string> bugIds)
        {
            labels.Clear();
            bugIds.Clear();
            foreach (var seed in Seeds)
            {
                if (seed == null || (seed is UnityEngine.Object unityObject && unityObject == null)) continue;
                if (!QALab.IsSeedEnabled(seed.BugId) || !VisualLabels.IsKnown(seed.Label)) continue;
                bool visible;
                try
                {
                    visible = seed.IsVisible(camera);
                }
                catch (Exception exc)
                {
                    Debug.LogWarning($"[QALab] a visual seed's visibility check failed: {exc.Message}");
                    continue;
                }
                if (!visible) continue;
                if (!labels.Contains(seed.Label)) labels.Add(seed.Label);
                if (!bugIds.Contains(seed.BugId)) bugIds.Add(seed.BugId);
            }
        }

        internal static void Clear() => Seeds.Clear();

        /// <summary>
        /// True when <paramref name="renderer"/> is in <paramref name="camera"/>'s view, covers at least
        /// <paramref name="minCoverage"/> of the screen and (with physics) nothing blocks the line from the
        /// camera to its centre.
        /// </summary>
        public static bool IsOnScreen(Renderer renderer, Camera camera, float minCoverage = DefaultMinCoverage)
        {
            if (renderer == null || camera == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) return false;
            var bounds = renderer.bounds;
            GeometryUtility.CalculateFrustumPlanes(camera, Planes);
            if (!GeometryUtility.TestPlanesAABB(Planes, bounds)) return false;
            if (ScreenCoverage(camera, bounds) < minCoverage) return false;
#if QALAB_PHYSICS
            var eye = camera.transform.position;
            if (Physics.Linecast(eye, bounds.center, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                var hitTransform = hit.collider.transform;
                if (hitTransform != renderer.transform && !hitTransform.IsChildOf(renderer.transform)
                    && !renderer.transform.IsChildOf(hitTransform))
                {
                    return false;   // something else is in the way
                }
            }
#endif
            return true;
        }

        /// <summary>Fraction of the screen (0–1) covered by the screen-space box around <paramref name="bounds"/>.</summary>
        public static float ScreenCoverage(Camera camera, Bounds bounds)
        {
            var min = bounds.min;
            var max = bounds.max;
            for (var i = 0; i < 8; i++)
            {
                Corners[i] = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
            }
            float x0 = 1f, y0 = 1f, x1 = 0f, y1 = 0f;
            var anyInFront = false;
            foreach (var corner in Corners)
            {
                var v = camera.WorldToViewportPoint(corner);
                if (v.z <= 0f) continue;   // behind the camera
                anyInFront = true;
                x0 = Math.Min(x0, v.x);
                y0 = Math.Min(y0, v.y);
                x1 = Math.Max(x1, v.x);
                y1 = Math.Max(y1, v.y);
            }
            if (!anyInFront) return 0f;
            var width = Mathf.Clamp01(x1) - Mathf.Clamp01(x0);
            var height = Mathf.Clamp01(y1) - Mathf.Clamp01(y0);
            return width > 0f && height > 0f ? width * height : 0f;
        }

        /// <summary>
        /// True when <paramref name="rect"/> (a UI element) is active and at least part of it is on screen.
        /// Works for Screen Space - Overlay and Camera canvases.
        /// </summary>
        public static bool IsOnScreen(RectTransform rect)
        {
            if (rect == null || !rect.gameObject.activeInHierarchy) return false;
            var canvas = rect.GetComponentInParent<Canvas>();
            if (canvas == null || !canvas.enabled) return false;
            var eye = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            rect.GetWorldCorners(UiCorners);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var corner in UiCorners)
            {
                var p = RectTransformUtility.WorldToScreenPoint(eye, corner);
                x0 = Math.Min(x0, p.x);
                y0 = Math.Min(y0, p.y);
                x1 = Math.Max(x1, p.x);
                y1 = Math.Max(y1, p.y);
            }
            return x1 > 0f && y1 > 0f && x0 < Screen.width && y0 < Screen.height && x1 > x0 && y1 > y0;
        }
    }
}
