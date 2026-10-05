using UnityEngine;

namespace MiawWorks.QALab
{
    /// <summary>
    /// F12 takes a manual screenshot (spec 01) in the editor and in development builds. Reads whichever
    /// input backend the project has enabled, so it never throws when only the Input System is active.
    /// </summary>
    internal static class ManualShotKey
    {
        public static bool WasPressed()
        {
            if (!Debug.isDebugBuild) return false;
#if QALAB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null && keyboard.f12Key.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.F12);
#else
            return false;
#endif
        }
    }
}
