using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB05 (Asset loading): gameplay code asks for sound effects that are not in the build, every few
    /// seconds, with a random number in the name (<c>Assets/Audio/sfx_17.wav</c>). Triage normalizes the
    /// number into one cluster; over-normalizing paths could also merge different bugs (DECISIONS.md).
    /// </summary>
    public sealed class AssetLoader : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float requestIntervalS = 12f;
        [SerializeField] private int randomSeed = 5;

        private System.Random _random;
        private float _nextRequest;
        private bool _requested;

        /// <summary>F1 menu: request a missing asset on the next frame.</summary>
        public void RequestMissingAsset() => _requested = true;

        private void Awake()
        {
            _random = new System.Random(randomSeed);
            _nextRequest = requestIntervalS;
        }

        private void Update()
        {
            if (Time.time < _nextRequest && !_requested) return;
            _requested = false;
            _nextRequest = Time.time + requestIntervalS;
            if (SandboxSeeds.IsEnabled("SB05"))
            {
                Load("Assets/Audio/sfx_" + _random.Next(1, 40) + ".wav");
            }
        }

        /// <summary>Stand-in for an on-demand loader: none of the sfx_N clips exist in the sandbox.</summary>
        public AudioClip Load(string path)
        {
            LabelRecorder.Trigger("SB05");
            Debug.LogError("Failed to load asset " + path);
            return null;   // the caller falls back to a default clip
        }
    }
}
