using UnityEngine;
#if QALAB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace QALab.Sandbox
{
    /// <summary>
    /// Keyboard input that works with either input backend. Unity 6's Universal 3D template enables
    /// only the new Input System, and the old <c>Input.GetKey</c> API throws there; 2022.3 projects
    /// often use the old one. QALAB_INPUT_SYSTEM is defined by the asmdef when the package is installed,
    /// ENABLE_INPUT_SYSTEM by Unity when it is the active backend.
    /// </summary>
    public static class SandboxInput
    {
#if QALAB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
        public static bool DebugMenuPressed => Pressed(Keyboard.current?.f1Key);
        public static bool InteractPressed => Pressed(Keyboard.current?.eKey);
        public static bool InventoryPressed => Pressed(Keyboard.current?.tabKey);

        public static Vector2 Move
        {
            get
            {
                var k = Keyboard.current;
                if (k == null) return Vector2.zero;
                var x = (k.dKey.isPressed ? 1f : 0f) - (k.aKey.isPressed ? 1f : 0f);
                var y = (k.wKey.isPressed ? 1f : 0f) - (k.sKey.isPressed ? 1f : 0f);
                return Vector2.ClampMagnitude(new Vector2(x, y), 1f);
            }
        }

        private static bool Pressed(UnityEngine.InputSystem.Controls.KeyControl key) => key != null && key.wasPressedThisFrame;
#else
        public static bool DebugMenuPressed => Input.GetKeyDown(KeyCode.F1);
        public static bool InteractPressed => Input.GetKeyDown(KeyCode.E);
        public static bool InventoryPressed => Input.GetKeyDown(KeyCode.Tab);

        public static Vector2 Move => Vector2.ClampMagnitude(new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")), 1f);
#endif
    }
}
