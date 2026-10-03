using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>Press Interact (E) within 2 m of a door to open it (design doc: "Doors").</summary>
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float rangeM = 2f;

        private GameObject _requested;

        /// <summary>F1 menu: interact with <paramref name="target"/> on the next frame (normal Update path).</summary>
        public void RequestInteract(GameObject target) => _requested = target;

        private void Update()
        {
            if (_requested != null)
            {
                var target = _requested;
                _requested = null;
                TryInteract(target);
            }
            else if (SandboxInput.InteractPressed)
            {
                TryInteractNearest(out _);
            }
        }

        /// <summary>Interact with the nearest door in range; used by the E key and the bot (IBotMover).</summary>
        public bool TryInteractNearest(out string objectName)
        {
            var door = NearestDoor();
            objectName = door != null ? door.name : null;
            return door != null && TryInteract(door.gameObject);
        }

        public bool TryInteract(GameObject target)
        {
            var door = target != null ? target.GetComponent<SeededDoor>() : null;
            if (door == null) return false;
            door.Open();
            return true;
        }

        private SeededDoor NearestDoor()
        {
            SeededDoor best = null;
            var bestDistance = rangeM;
            foreach (var door in SeededDoor.All)
            {
                var distance = Vector3.Distance(transform.position, door.transform.position);
                if (distance <= bestDistance)
                {
                    best = door;
                    bestDistance = distance;
                }
            }
            return best;
        }
    }
}
