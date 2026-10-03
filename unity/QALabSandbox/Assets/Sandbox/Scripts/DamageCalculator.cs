namespace QALab.Sandbox
{
    /// <summary>Damage per second of a weapon. One of the two SafeDivide callers behind SB14.</summary>
    public sealed class DamageCalculator
    {
        public float Compute(Weapon weapon)
        {
            return MathUtil.SafeDivide(weapon.DamagePerShot, weapon.FireIntervalS, "Damage");
        }
    }
}
