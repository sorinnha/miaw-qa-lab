using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// F1 debug menu: trigger any M1 seed by hand. Each button asks the owning script to act on its next
    /// Update, so the logged stack is the same as in normal play (a different caller would change the
    /// top frames and split triage clusters).
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

        private bool _open;
        private Rect _window = new Rect(20, 20, 300, 330);

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
            GUILayout.Space(8);
            if (GUILayout.Button("Trigger all")) TriggerAll();
            if (GUILayout.Button("End run now")) MiawWorks.QALab.QALab.EndRun(MiawWorks.QALab.ExitReasons.UserQuit);
            GUI.DragWindow();
        }

        private void TriggerAll()
        {
            interactor.RequestInteract(brokenDoor);
            hud.RequestOpen();
            spawner.RequestWave();   // odd wave: SB04
            spawner.RequestWave();   // even wave: SB03
            assetLoader.RequestMissingAsset();
            player.RequestStep("Gravel");
            combat.RequestBoth();
        }
    }
}
