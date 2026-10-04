namespace QALab.Sandbox
{
    /// <summary>Weapon data. A fire interval of 0 is invalid data (see "Combat math" in the design doc).</summary>
    public sealed class Weapon
    {
        public Weapon(string name, float damagePerShot, float fireIntervalS)
        {
            Name = name;
            DamagePerShot = damagePerShot;
            FireIntervalS = fireIntervalS;
        }

        public string Name { get; }
        public float DamagePerShot { get; }
        public float FireIntervalS { get; }
    }
}
