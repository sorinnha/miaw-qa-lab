using System.Collections;
using System.Collections.Generic;
using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB01 (Doors): Door_02 was placed without its hinge, so <c>Open()</c> throws
    /// NullReferenceException. The swing logic is a plain C# object built from the hinge in Awake: a
    /// missing serialized Transform would raise Unity's UnassignedReferenceException in the editor, but a
    /// null plain object raises the real NullReferenceException a player build shows.
    /// </summary>
    public sealed class SeededDoor : MonoBehaviour
    {
        private static readonly List<SeededDoor> AllDoors = new List<SeededDoor>();

        [SerializeField] private Transform hinge;
        [SerializeField, Min(0.1f)] private float swingSeconds = 0.5f;
        [SerializeField, Min(0.1f)] private float openSeconds = 4f;

        private DoorSwing _swing;
        private bool _busy;

        public static IReadOnlyList<SeededDoor> All => AllDoors;

        private void Awake()
        {
            _swing = hinge != null ? new DoorSwing(hinge) : null;
        }

        private void OnEnable() => AllDoors.Add(this);
        private void OnDisable() => AllDoors.Remove(this);

        /// <summary>Called by <see cref="Interactor"/> when the player presses Interact within 2 m.</summary>
        public void Open()
        {
            if (_busy) return;
            if (_swing == null)
            {
                if (!SandboxSeeds.IsEnabled("SB01"))
                {
                    _swing = new DoorSwing(transform);   // the fixed behaviour: swing around the door itself
                }
                else
                {
                    LabelRecorder.Trigger("SB01");
                }
            }
            var target = _swing.OpenRotation();   // SB01: _swing is null on Door_02 → NullReferenceException
            StartCoroutine(SwingOpenThenClose(target));
        }

        private IEnumerator SwingOpenThenClose(Quaternion open)
        {
            _busy = true;
            yield return _swing.RotateTo(open, swingSeconds);
            yield return new WaitForSeconds(openSeconds);
            yield return _swing.RotateTo(_swing.ClosedRotation, swingSeconds);
            _busy = false;
        }

        /// <summary>Rotates a hinge 90° over a short time.</summary>
        private sealed class DoorSwing
        {
            private readonly Transform _pivot;

            public DoorSwing(Transform pivot)
            {
                _pivot = pivot;
                ClosedRotation = pivot.localRotation;
            }

            public Quaternion ClosedRotation { get; }

            public Quaternion OpenRotation() => ClosedRotation * Quaternion.Euler(0f, 90f, 0f);

            public IEnumerator RotateTo(Quaternion target, float seconds)
            {
                var start = _pivot.localRotation;
                for (var t = 0f; t < seconds; t += Time.deltaTime)
                {
                    _pivot.localRotation = Quaternion.Slerp(start, target, t / seconds);
                    yield return null;
                }
                _pivot.localRotation = target;
            }
        }
    }
}
