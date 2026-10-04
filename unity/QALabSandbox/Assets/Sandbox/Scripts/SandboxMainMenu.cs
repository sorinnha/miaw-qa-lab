using UnityEngine;
using UnityEngine.SceneManagement;

namespace QALab.Sandbox
{
    /// <summary>Main menu (Play, Settings, Credits, Quit) and its Settings panel; the M4 UI crawler clicks these.</summary>
    public sealed class SandboxMainMenu : MonoBehaviour
    {
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject creditsPanel;
        [SerializeField] private string levelScene = "Sandbox_Level01";

        public void Play() => SceneManager.LoadScene(levelScene);

        public void ShowSettings() => Show(settingsPanel);

        public void ShowCredits() => Show(creditsPanel);

        public void ShowMain() => Show(mainPanel);

        /// <summary>Applies settings (nothing to apply yet; SB15 adds a seeded failure here in M4).</summary>
        public void ApplySettings() => Show(mainPanel);

        public void Quit() => Application.Quit();

        private void Show(GameObject panel)
        {
            mainPanel.SetActive(panel == mainPanel);
            settingsPanel.SetActive(panel == settingsPanel);
            creditsPanel.SetActive(panel == creditsPanel);
        }
    }
}
