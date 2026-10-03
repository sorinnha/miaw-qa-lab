using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>Keeps the camera at a fixed offset above and behind the player.</summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 12f, -8f);

        public void SetTarget(Transform value) => target = value;

        private void LateUpdate()
        {
            if (target == null) return;
            transform.position = target.position + offset;
            transform.LookAt(target.position);
        }
    }
}
