using System.Collections.Generic;
using MiawWorks.QALab;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB03 (Enemy registry): <c>Get</c> uses the dictionary indexer, so an unknown id throws
    /// KeyNotFoundException instead of returning null as the design doc requires. The ids are random
    /// hex (<c>enemy_4f2a9c1e</c>), which triage must normalize into one cluster.
    /// </summary>
    public sealed class SeededEnemyRegistry
    {
        private readonly Dictionary<string, Enemy> _enemies = new Dictionary<string, Enemy>();

        public int Count => _enemies.Count;

        public void Register(Enemy enemy) => _enemies[enemy.Id] = enemy;

        public void Unregister(string id) => _enemies.Remove(id);

        public Enemy Get(string id)
        {
            if (!SandboxSeeds.IsEnabled("SB03"))
            {
                return _enemies.TryGetValue(id, out var found) ? found : null;   // the fixed behaviour
            }
            if (!_enemies.ContainsKey(id))
            {
                LabelRecorder.Trigger("SB03");
            }
            return _enemies[id];   // SB03: KeyNotFoundException for unknown ids
        }
    }
}
