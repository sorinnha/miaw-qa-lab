using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// Takes the screenshots the <see cref="ShotPlanner"/> plans (spec 01, "Screenshots"). Capture waits
    /// for the end of the frame (<c>WaitForEndOfFrame</c>), when the frame is fully drawn:
    /// <list type="bullet">
    /// <item><c>screen_capture</c> (default): <c>ScreenCapture.CaptureScreenshotAsTexture()</c>, the frame
    /// as the player saw it, Screen Space - Overlay UI included.</item>
    /// <item><c>camera_render</c> (batch mode, where there is no screen): <c>Camera.main</c> rendered into a
    /// texture. Overlay UI is missing; the event records the method so vision knows.</item>
    /// </list>
    /// Images are scaled so the long side is at most 1280 px and saved as <c>shots/NNNNNN.png</c>, then a
    /// <c>screenshot</c> event is written. In benchmark runs the visual seeds are asked what is on screen
    /// in the same frame, and the answer goes to labels.json. Encoding a PNG takes milliseconds, so the
    /// host leaves the next frame out of the frame-time stats (the observer effect).
    /// In batch mode there is no screen and no end-of-frame rendering, so the host captures right away
    /// (<see cref="Capture"/>), and without a camera there is nothing to capture: no shot is planned
    /// (<see cref="CanCapture"/>), which is not an error.
    /// </summary>
    internal sealed class ScreenshotService
    {
        private readonly string _runDir;
        private readonly EventWriter _writer;
        private readonly IClock _clock;
        private readonly Action _onCaptured;
        private readonly Action<string> _onError;
        private readonly List<string> _labels = new List<string>();
        private readonly List<string> _bugIds = new List<string>();

        /// <param name="onCaptured">Called after every capture attempt (the next frame's time includes it).</param>
        /// <param name="onError">Called with a message when a capture fails (a QA Lab internal error).</param>
        public ScreenshotService(string runDir, EventWriter writer, IClock clock, Action onCaptured, Action<string> onError)
        {
            _runDir = runDir;
            _writer = writer;
            _clock = clock;
            _onCaptured = onCaptured;
            _onError = onError;
            Directory.CreateDirectory(Path.Combine(runDir, Shots.Folder));
        }

        /// <summary>
        /// False in batch mode without a <c>MainCamera</c>-tagged camera: <c>camera_render</c> has nothing
        /// to render. The host plans no shot then, so detector events never point to a missing file.
        /// </summary>
        public static bool CanCapture => !Application.isBatchMode || Camera.main != null;

        /// <summary>A coroutine: waits for the end of this frame, then captures <paramref name="shot"/>.</summary>
        public IEnumerator CaptureAtEndOfFrame(PlannedShot shot)
        {
            yield return new WaitForEndOfFrame();
            Capture(shot);
        }

        /// <summary>Capture now (the frame must be finished: call at the end of a frame).</summary>
        public void Capture(PlannedShot shot)
        {
            var t = _clock.Seconds;
            var camera = Camera.main;
            if (LabelRecorder.IsRecording)
            {
                VisualLabelProbe.Probe(camera, _labels, _bugIds);   // what is visible in this very frame
            }
            Texture2D image = null;
            try
            {
                string method;
                if (Application.isBatchMode)
                {
                    image = RenderCamera(camera);
                    method = Shots.CameraRender;
                }
                else
                {
                    image = ScreenCapture.CaptureScreenshotAsTexture();
                    method = Shots.ScreenCapture;
                }
                if (image == null) throw new InvalidOperationException("no image (is there a main camera?)");
                var (width, height) = Shots.Fit(image.width, image.height);
                if (width != image.width || height != image.height)
                {
                    var scaled = Scale(image, width, height);
                    UnityEngine.Object.Destroy(image);
                    image = scaled;
                }
                var full = Path.Combine(_runDir, shot.Path.Replace('/', Path.DirectorySeparatorChar));
                File.WriteAllBytes(full, ImageConversion.EncodeToPNG(image));
                if (LabelRecorder.IsRecording)
                {
                    LabelRecorder.RecordShot(shot.Path, t, _labels, _bugIds);
                }
                _writer.Enqueue(EventKinds.Screenshot, Shots.Data(shot.Path, shot.Reason, width, height, method));
            }
            catch (Exception exc)
            {
                _onError?.Invoke($"screenshot {shot.Path}: {exc.GetType().Name}: {exc.Message}");
            }
            finally
            {
                if (image != null) UnityEngine.Object.Destroy(image);
                _onCaptured?.Invoke();
            }
        }

        private static Texture2D RenderCamera(Camera camera)
        {
            if (camera == null) return null;
            var (width, height) = Shots.Fit(Math.Max(1, Screen.width), Math.Max(1, Screen.height));
            var target = RenderTexture.GetTemporary(width, height, 24);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }

        // GPU resize: draw the image into a smaller render texture and read it back.
        private static Texture2D Scale(Texture2D source, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 0);
            var previousActive = RenderTexture.active;
            try
            {
                Graphics.Blit(source, target);
                RenderTexture.active = target;
                var image = new Texture2D(width, height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                return image;
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
            }
        }
    }
}
