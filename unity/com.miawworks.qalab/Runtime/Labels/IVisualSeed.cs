using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// A seeded visual bug that can tell whether it is on screen (benchmark mode, spec 01 "Labels").
    /// Seeds register with <see cref="VisualLabelProbe.Register"/> in OnEnable and unregister in OnDisable.
    /// Before every screenshot the probe asks each enabled seed <see cref="IsVisible"/>; the answers become
    /// that shot's labels in labels.json. They are ground truth for <c>qalab eval</c> only and never reach
    /// events.jsonl.
    /// </summary>
    public interface IVisualSeed
    {
        /// <summary>The seed id, e.g. <c>SB09</c>.</summary>
        string BugId { get; }

        /// <summary>One of <see cref="VisualLabels"/>.</summary>
        string Label { get; }

        /// <summary>
        /// Is the bug visible in the frame being captured? Called at the end of the frame, right before the
        /// screenshot, with the camera that renders the game (<c>Camera.main</c>, may be null).
        /// </summary>
        bool IsVisible(Camera camera);
    }
}
