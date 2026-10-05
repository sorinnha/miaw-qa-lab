using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// One ballistics-range projectile. It stops (is destroyed) when it hits something. If it is ever past
    /// the thin wall, it went through it: SB16 happened (ground truth for labels.json).
    /// </summary>
    public sealed class Projectile : MonoBehaviour
    {
        private float _wallX;
        private float _dieAt;

        public void Launch(float wallX, float lifetimeS)
        {
            _wallX = wallX;
            _dieAt = Time.time + lifetimeS;
        }

        private void Update()
        {
            if (transform.position.x > _wallX + 0.5f)
            {
                LabelRecorder.Trigger("SB16");
                Destroy(gameObject);
            }
            else if (Time.time >= _dieAt)
            {
                Destroy(gameObject);
            }
        }

        private void OnCollisionEnter(Collision collision) => Destroy(gameObject);
    }
}
