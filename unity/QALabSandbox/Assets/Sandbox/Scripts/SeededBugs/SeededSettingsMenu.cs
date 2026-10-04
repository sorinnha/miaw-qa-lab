using System;
using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB15 (Settings menu): the Settings panel's Apply handler. The design doc says Apply must work from
    /// any state, including when nothing changed; with the seed on it throws InvalidOperationException
    /// when there is nothing to apply, which is exactly what a random UI crawler does (it opens Settings
    /// and presses Apply). The exception escapes into the button's click event, where Unity logs it.
    /// </summary>
    public sealed class SeededSettingsMenu : MonoBehaviour
    {
        private const string VolumeKey = "sandbox.volume";

        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        private float _appliedVolume = -1f;

        /// <summary>A pending change (no slider in the panel yet; tests and future controls use it).</summary>
        public void SetVolume(float value) => volume = Mathf.Clamp01(value);

        public bool HasPendingChanges => !Mathf.Approximately(volume, _appliedVolume) && _appliedVolume >= 0f;

        private void Awake() => _appliedVolume = PlayerPrefs.GetFloat(VolumeKey, volume);

        /// <summary>Called by the Apply button (through <see cref="SandboxMainMenu.ApplySettings"/>).</summary>
        public void Apply()
        {
            if (!HasPendingChanges && SandboxSeeds.IsEnabled("SB15"))
            {
                LabelRecorder.Trigger("SB15");
                throw new InvalidOperationException("Cannot apply settings: there are no pending changes");
            }
            _appliedVolume = volume;
            PlayerPrefs.SetFloat(VolumeKey, volume);
            AudioListener.volume = volume;
        }
    }
}
