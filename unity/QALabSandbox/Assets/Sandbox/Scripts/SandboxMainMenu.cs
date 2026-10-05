using UnityEngine;
using UnityEngine.SceneManagement;

namespace QALab.Sandbox
{
    /// <summary>Main menu (Play, Settings, Credits, Quit) and its Settings panel; the UI crawler clicks these.</summary>
    public sealed class SandboxMainMenu : MonoBehaviour
    {
        [SerializeField] private GameObject mainPanel;
        [SerializeField] private GameObject settingsPanel;
        [SerializeField] private GameObject creditsPanel;
        [SerializeField] private SeededSettingsMenu settings;
        [SerializeField] private string levelScene = "Sandbox_Level01";

        public void Play() => SceneManager.LoadScene(levelScene);

        public void ShowSettings() => Show(settingsPanel);

        public void ShowCredits() => Show(creditsPanel);

        public void ShowMain() => Show(mainPanel);

        /// <summary>Apply the settings and go back to the main panel (SB15 throws in Apply).</summary>
        public void ApplySettings()
        {
            if (settings != null) settings.Apply();
            Show(mainPanel);
        }

        public void Quit() => Application.Quit();

        private void Show(GameObject panel)
        {
            mainPanel.SetActive(panel == mainPanel);
            settingsPanel.SetActive(panel == settingsPanel);
            creditsPanel.SetActive(panel == creditsPanel);
        }
    }
}
