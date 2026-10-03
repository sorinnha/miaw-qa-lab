using System.Collections.Generic;
using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB13 (Audio): Gravel and Metal have no footstep clip, and the warning is logged on every step
    /// instead of once per surface. Lots of low-severity noise: triage should rank it last.
    /// With SB13 off the sandbox behaves as the design doc says (every surface has a clip), so nothing
    /// is logged: any "Footstep audio clip missing" line in a run means SB13 was on.
    /// </summary>
    public sealed class FootstepAudio : MonoBehaviour
    {
        private static readonly HashSet<string> SurfacesWithClips = new HashSet<string> { "Grass", "Wood" };

        public void Play(string surface)
        {
            if (SurfacesWithClips.Contains(surface) || !SandboxSeeds.IsEnabled("SB13"))
            {
                return;   // a real clip would play here
            }
            LabelRecorder.Trigger("SB13");
            Debug.LogWarning($"Footstep audio clip missing for surface '{surface}'");
        }
    }
}
