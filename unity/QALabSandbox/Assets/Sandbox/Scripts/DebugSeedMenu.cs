using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// F1 debug menu: trigger any seed by hand. Log seeds: each button asks the owning script to act on its
    /// next Update, so the logged stack is the same as in normal play (a different caller would change the
    /// top frames and split triage clusters). Level and visual seeds: the button either fires the seed
    /// (GC burst, blackout, points, a shot) or takes the player next to it. SB15 lives in Sandbox_Menu:
    /// open Settings there and press Apply.
    /// </summary>
    public sealed class DebugSeedMenu : MonoBehaviour
    {
        [SerializeField] private Interactor interactor;
        [SerializeField] private GameObject brokenDoor;          // Door_02
        [SerializeField] private HudInventory hud;
        [SerializeField] private SeededSpawner spawner;
        [SerializeField] private AssetLoader assetLoader;
        [SerializeField] private SandboxPlayer player;
        [SerializeField] private CombatDriver combat;
        [SerializeField] private GcZone gcZone;
        [SerializeField] private CameraZone cameraZone;
        [SerializeField] private HudScore score;
        [SerializeField] private ProjectileLauncher launcher;

        private bool _open;
        private Rect _window = new Rect(20, 20, 330, 560);

        private void Update()
        {
            if (SandboxInput.DebugMenuPressed) _open = !_open;
        }

        private void OnGUI()
        {
            if (_open) _window = GUILayout.Window(GetInstanceID(), _window, DrawWindow, "Seeded bugs (F1)");
        }

        private void DrawWindow(int id)
        {
            var dir = MiawWorks.QALab.QALab.RunDir;
            GUILayout.Label(dir != null ? "QA Lab recording:\n" + dir : "QA Lab is off (-qalab or settings asset)");
            if (GUILayout.Button("SB01  Open Door_02")) interactor.RequestInteract(brokenDoor);
            if (GUILayout.Button("SB02  Open inventory")) hud.RequestOpen();
            if (GUILayout.Button("SB04/SB03  Spawn a wave (odd: SB04, even: SB03)")) spawner.RequestWave();
            if (GUILayout.Button("SB05  Load a missing sound")) assetLoader.RequestMissingAsset();
            if (GUILayout.Button("SB13  Step on gravel")) player.RequestStep("Gravel");
            if (GUILayout.Button("SB14  Damage + speed math")) combat.RequestBoth();
            GUILayout.Space(6);
            if (GUILayout.Button("SB06  Stand on tile T_17")) player.TeleportTo(TileTop(SandboxLayout.HoleTile));
            if (GUILayout.Button("SB07  Go to the narrow gap (walk north)"))
            {
                player.TeleportTo(new Vector3(SandboxLayout.GapCenterX, 1.1f, SandboxLayout.NorthWestRoomZ0 - 2f));
            }
            if (GUILayout.Button("SB08  GC burst now")) gcZone.RequestBurst();
            // Beside the crate, not behind it: the follow camera then sees it at about 2% of the screen,
            // with the player off the line of sight.
            if (GUILayout.Button("SB09  Look at Crate_07")) player.TeleportTo(new Vector3(31.1f, 1.1f, 27.5f));
            if (GUILayout.Button("SB10  Black out the camera now")) cameraZone.RequestBlackout();
            if (GUILayout.Button("SB11  +10000 points")) score.AddPoints(10000);
            if (GUILayout.Button("SB12/SB16  Go to the ballistics range"))
            {
                player.TeleportTo(new Vector3(SandboxLayout.LauncherX, 1.1f, SandboxLayout.LauncherZ - 4f));
            }
            if (GUILayout.Button("SB16  Fire one projectile")) launcher.RequestShot();
            if (GUILayout.Button("Screenshot (F12)")) MiawWorks.QALab.QALab.RequestScreenshot();
            GUILayout.Space(6);
            if (GUILayout.Button("Trigger all (here)")) TriggerAll();
            if (GUILayout.Button("End run now")) MiawWorks.QALab.QALab.EndRun(MiawWorks.QALab.ExitReasons.UserQuit);
            GUI.DragWindow();
        }

        private static Vector3 TileTop(int tile) =>
            new Vector3(SandboxLayout.TileCenterX(tile), 1.1f, SandboxLayout.TileCenterZ(tile));

        /// <summary>Every seed that can fire where the player stands (no teleports).</summary>
        private void TriggerAll()
        {
            interactor.RequestInteract(brokenDoor);
            hud.RequestOpen();
            spawner.RequestWave();   // odd wave: SB04
            spawner.RequestWave();   // even wave: SB03
            assetLoader.RequestMissingAsset();
            player.RequestStep("Gravel");
            combat.RequestBoth();
            gcZone.RequestBurst();
            cameraZone.RequestBlackout();
            score.AddPoints(10000);
            launcher.RequestShot();
        }
    }
}
