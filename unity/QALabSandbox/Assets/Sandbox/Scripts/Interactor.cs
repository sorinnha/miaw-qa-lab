using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// Press Interact (E) within 2 m of a door to open it (design doc: "Doors"). Every way of
    /// interacting (E key, F1 menu, the M4 bot via <see cref="SandboxPlayer"/>) only queues a target;
    /// <see cref="Update"/> then calls <see cref="TryInteract"/>. So SB01's stack is always
    /// <c>SeededDoor.Open ← Interactor.TryInteract ← Interactor.Update</c> and triage sees one cluster.
    /// </summary>
    public sealed class Interactor : MonoBehaviour
    {
        [SerializeField, Min(0.5f)] private float rangeM = 2f;

        private GameObject _requested;

        /// <summary>Interact with <paramref name="target"/> on the next frame (F1 menu).</summary>
        public void RequestInteract(GameObject target) => _requested = target;

        /// <summary>Queue the nearest door in range for the next frame; false when nothing is in range.</summary>
        public bool RequestInteractNearest(out string objectName)
        {
            var door = NearestDoor();
            objectName = door != null ? door.name : null;
            if (door == null) return false;
            _requested = door.gameObject;
            return true;
        }

        private void Update()
        {
            if (SandboxInput.InteractPressed)
            {
                RequestInteractNearest(out _);
            }
            if (_requested != null)
            {
                var target = _requested;
                _requested = null;
                TryInteract(target);
            }
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
