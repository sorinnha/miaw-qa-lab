using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB07 (Level geometry): a post narrows the north-west room's doorway to 0.76 m. The NavMesh agent
    /// (radius 0.3 m) fits, so the bot plans a path through it; the player (radius 0.4 m) doesn't, so it
    /// pushes against the gap. QA Lab's stuck detector should report <c>stuck</c> at the gap. With the
    /// seed off the post is removed and the doorway is 1.6 m wide. The seed counts as triggered when the
    /// player stays within 1.5 m of the gap for 2 s.
    /// </summary>
    public sealed class SeededNarrowGap : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float nearRadiusM = 1.5f;
        [SerializeField, Min(0.1f)] private float stuckSeconds = 2f;

        private float _nearFor;
        private bool _triggered;

        private void Awake()
        {
            if (!SandboxSeeds.IsEnabled("SB07"))
            {
                gameObject.SetActive(false);   // no post: the doorway is wide enough
            }
        }

        private void Update()
        {
            var player = SandboxSeeds.Player;
            if (player == null) return;
            var p = player.position;
            var dx = p.x - SandboxLayout.GapCenterX;
            var dz = p.z - SandboxLayout.NorthWestRoomZ0;
            if (dx * dx + dz * dz > nearRadiusM * nearRadiusM)
            {
                _nearFor = 0f;
                _triggered = false;
                return;
            }
            _nearFor += Time.deltaTime;
            if (_nearFor >= stuckSeconds && !_triggered)
            {
                _triggered = true;
                LabelRecorder.Trigger("SB07");
            }
        }
    }
}
