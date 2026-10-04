using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>Third-person follow camera: 6 m behind and 2.5 m above the player (design doc: "Camera").</summary>
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 2.5f, -6f);

        public void SetTarget(Transform value) => target = value;

        private void LateUpdate()
        {
            if (target == null) return;
            transform.position = target.position + offset;
            transform.LookAt(target.position + Vector3.up);   // look at chest height, not the feet
        }
    }
}
