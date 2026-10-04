using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// Runs the combat math every few seconds, the way weapon fire and speed boosts would. With SB14 on,
    /// the prototype weapon (fire interval 0) and the boost (duration 0) are invalid data.
    /// </summary>
    public sealed class CombatDriver : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float damageIntervalS = 7f;
        [SerializeField, Min(1f)] private float speedIntervalS = 9f;

        private readonly DamageCalculator _damage = new DamageCalculator();
        private float _nextDamage;
        private float _nextSpeed;
        private bool _damageRequested;
        private bool _speedRequested;

        /// <summary>F1 menu: run both calculations on the next frame.</summary>
        public void RequestBoth()
        {
            _damageRequested = true;
            _speedRequested = true;
        }

        private void Awake()
        {
            _nextDamage = damageIntervalS;
            _nextSpeed = speedIntervalS;
        }

        private void Update()
        {
            var broken = SandboxSeeds.IsEnabled("SB14");
            if (_damageRequested || Time.time >= _nextDamage)
            {
                _damageRequested = false;
                _nextDamage = Time.time + damageIntervalS;
                var weapon = broken ? new Weapon("Prototype", 12f, 0f) : new Weapon("Rifle", 12f, 0.25f);
                _damage.Compute(weapon);
            }
            if (_speedRequested || Time.time >= _nextSpeed)
            {
                _speedRequested = false;
                _nextSpeed = Time.time + speedIntervalS;
                new SpeedModel(4.5f, 2f, broken ? 0f : 3f).GetSpeed();
            }
        }
    }
}
