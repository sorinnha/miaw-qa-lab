using System.Collections.Generic;
using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB13 (Audio): Gravel and Metal have no footstep clip, and the warning is logged on every step
    /// instead of once per surface. Lots of low-severity noise: triage should rank it last.
    /// </summary>
    public sealed class FootstepAudio : MonoBehaviour
    {
        private static readonly HashSet<string> SurfacesWithClips = new HashSet<string> { "Grass", "Wood" };
        private readonly HashSet<string> _reported = new HashSet<string>();

        public void Play(string surface)
        {
            if (SurfacesWithClips.Contains(surface))
            {
                return;   // a real clip would play here
            }
            if (!SandboxSeeds.IsEnabled("SB13") && !_reported.Add(surface))
            {
                return;   // the fixed behaviour: report once per surface
            }
            if (SandboxSeeds.IsEnabled("SB13"))
            {
                LabelRecorder.Trigger("SB13");
            }
            Debug.LogWarning($"Footstep audio clip missing for surface '{surface}'");
        }
    }
}
