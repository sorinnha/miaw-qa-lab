using System.Collections.Generic;
using MiawWorks.QALab;
using UnityEngine;

namespace QALab.Sandbox
{
    /// <summary>
    /// SB04 (Spawner): one spawn point was removed from the level but is still in the list, and
    /// <c>SpawnWave</c> doesn't check it → NullReferenceException with the same message as SB01 but a
    /// different stack. Waves alternate: odd waves hit the removed point (SB04), even waves spawn, and
    /// then <c>Update</c> assigns targets, where the registry lookup fails (SB03).
    /// Targets are assigned from <c>Update</c>, not inside <c>SpawnWave</c>: SB03's stack
    /// (<c>Get ← AssignTarget ← Update</c>) must not contain <c>SeededSpawner.SpawnWave</c>, or it would
    /// also match SB04's catalog rule and one exception would count as two seeded bugs.
    /// </summary>
    public sealed class SeededSpawner : MonoBehaviour
    {
        [SerializeField] private Transform[] spawnPoints = new Transform[0];
        [Tooltip("How many entries of the list point at a spawn point that was deleted from the level.")]
        [SerializeField, Min(0)] private int removedSpawnPoints = 1;
        [SerializeField, Min(1f)] private float waveIntervalS = 10f;
        [SerializeField, Min(1f)] private float activationRangeM = 25f;
        [SerializeField] private int randomSeed = 7;

        private readonly SeededEnemyRegistry _registry = new SeededEnemyRegistry();
        private readonly List<SpawnPoint> _points = new List<SpawnPoint>();
        private System.Random _random;
        private float _nextWave;
        private int _wave;
        private int _wavesRequested;

        /// <summary>F1 menu: queue a wave; queued waves run one per frame through the normal Update path.</summary>
        public void RequestWave() => _wavesRequested++;

        private void Awake()
        {
            _random = new System.Random(randomSeed);
            // Plain C# objects: a null entry is a real null (Unity's serialized "missing" objects would
            // raise MissingReferenceException in the editor instead of the player's NullReferenceException).
            foreach (var point in spawnPoints)
            {
                if (point != null) _points.Add(new SpawnPoint(point.position));
            }
            for (var i = 0; i < removedSpawnPoints; i++)
            {
                _points.Add(null);
            }
            _nextWave = waveIntervalS;
        }

        private void Update()
        {
            var due = Time.time >= _nextWave && PlayerInRange();
            if (!due && _wavesRequested == 0) return;
            if (!due) _wavesRequested--;
            _nextWave = Time.time + waveIntervalS;
            var spawned = SpawnWave();
            foreach (var enemy in spawned)
            {
                AssignTarget(enemy);
            }
        }

        /// <summary>Spawn and register 3–5 enemies; the caller assigns their targets.</summary>
        public List<Enemy> SpawnWave()
        {
            _wave++;
            var spawned = new List<Enemy>();
            var count = _random.Next(3, 6);   // 3–5 enemies, per the design doc
            for (var i = 0; i < count; i++)
            {
                var point = PickPoint(i);
                if (point == null)
                {
                    if (!SandboxSeeds.IsEnabled("SB04")) continue;   // the fixed behaviour: skip it
                    LabelRecorder.Trigger("SB04");
                }
                var position = point.Position;   // SB04: the removed point is null → NullReferenceException
                var enemy = new Enemy(NewEnemyId());
                _registry.Register(enemy);
                spawned.Add(enemy);
                Debug.DrawRay(position, Vector3.up * 2f, Color.red, 1f);
            }
            return spawned;
        }

        public void AssignTarget(Enemy enemy)
        {
            // Bug behind SB03: the "target" id is generated instead of picked from registered enemies.
            var targetId = SandboxSeeds.IsEnabled("SB03") ? NewEnemyId() : enemy.Id;
            var target = _registry.Get(targetId);
            enemy.TargetId = target?.Id;
        }

        /// <summary>Odd waves start at the removed point (SB04); even waves only use real points.</summary>
        private SpawnPoint PickPoint(int index)
        {
            var valid = _points.FindAll(p => p != null);
            if (_wave % 2 == 1 && index == 0 && removedSpawnPoints > 0) return null;
            return valid.Count == 0 ? null : valid[index % valid.Count];
        }

        private bool PlayerInRange()
        {
            var player = MiawWorks.QALab.QALab.Player;
            return player == null || Vector3.Distance(player.position, transform.position) <= activationRangeM;
        }

        private string NewEnemyId() => "enemy_" + _random.Next().ToString("x8");

        private sealed class SpawnPoint
        {
            public SpawnPoint(Vector3 position) { Position = position; }
            public Vector3 Position { get; }
        }
    }
}
