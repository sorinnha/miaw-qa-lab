namespace QALab.Sandbox
{
    /// <summary>Movement speed with a timed boost. The second SafeDivide caller behind SB14.</summary>
    public sealed class SpeedModel
    {
        private readonly float _baseSpeed;
        private readonly float _boostAmount;
        private readonly float _boostDurationS;

        public SpeedModel(float baseSpeed, float boostAmount, float boostDurationS)
        {
            _baseSpeed = baseSpeed;
            _boostAmount = boostAmount;
            _boostDurationS = boostDurationS;
        }

        /// <summary>Base speed plus the boost spread over its duration (a zero duration is invalid data).</summary>
        public float GetSpeed()
        {
            return _baseSpeed + MathUtil.SafeDivide(_boostAmount, _boostDurationS, "Movement speed");
        }
    }
}
