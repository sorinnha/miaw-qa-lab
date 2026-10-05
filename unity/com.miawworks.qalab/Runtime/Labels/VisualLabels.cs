// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;

namespace MiawWorks.QALab
{
    /// <summary>The visual label vocabulary v1 (spec 00, schemas/labels.schema.json → visualLabel).</summary>
    public static class VisualLabels
    {
        /// <summary>A renderer without a material: Unity draws it magenta.</summary>
        public const string MissingTexture = "missing_texture";

        /// <summary>The 3D view isn't rendering (the screen is black apart from the HUD).</summary>
        public const string BlackScreen = "black_screen";

        /// <summary>Text spills out of its UI box.</summary>
        public const string UiOverflow = "ui_overflow";

        /// <summary>A UI image without a sprite: a plain white box.</summary>
        public const string PlaceholderUi = "placeholder_ui";

        public static readonly string[] All = { MissingTexture, BlackScreen, UiOverflow, PlaceholderUi };

        public static bool IsKnown(string label) => Array.IndexOf(All, label) >= 0;
    }
}
