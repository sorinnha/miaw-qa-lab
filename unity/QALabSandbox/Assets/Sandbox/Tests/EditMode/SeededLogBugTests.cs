// Unity-only (uses UnityEngine): runs in Unity's Test Runner, not in tools/cs-check.
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace QALab.Sandbox.Tests
{
    /// <summary>
    /// The M1 log seeds produce exactly the messages and exceptions the catalog and the triage tests
    /// expect. QA Lab is not running here, so every seed is enabled and LabelRecorder is a no-op.
    /// </summary>
    public class SeededLogBugTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        private T Add<T>() where T : Component
        {
            var go = new GameObject(typeof(T).Name);
            _objects.Add(go);
            return go.AddComponent<T>();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) Object.DestroyImmediate(go);
            _objects.Clear();
        }

        [Test]
        public void Sb14SafeDivideLogsTheContextAndReturnsZero()
        {
            LogAssert.Expect(LogType.Error, "Damage: division by zero in SafeDivide");
            Assert.AreEqual(0f, new DamageCalculator().Compute(new Weapon("Prototype", 12f, 0f)));
            LogAssert.Expect(LogType.Error, "Movement speed: division by zero in SafeDivide");
            Assert.AreEqual(4.5f, new SpeedModel(4.5f, 2f, 0f).GetSpeed());
        }

        [Test]
        public void Sb14ValidDataDividesWithoutLogging()
        {
            Assert.AreEqual(48f, new DamageCalculator().Compute(new Weapon("Rifle", 12f, 0.25f)));
            Assert.AreEqual(5.5f, MathUtil.SafeDivide(11f, 2f, "test"));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Sb05MissingAssetLogsOneErrorWithThePath()
        {
            var loader = Add<AssetLoader>();
            LogAssert.Expect(LogType.Error, "Failed to load asset Assets/Audio/sfx_17.wav");
            Assert.IsNull(loader.Load("Assets/Audio/sfx_17.wav"));
        }

        [Test]
        public void Sb03UnknownEnemyIdThrowsKeyNotFound()
        {
            var registry = new SeededEnemyRegistry();
            var enemy = new Enemy("enemy_0000abcd");
            registry.Register(enemy);
            Assert.AreSame(enemy, registry.Get("enemy_0000abcd"));
            Assert.Throws<KeyNotFoundException>(() => registry.Get("enemy_4f2a9c1e"));
            registry.Unregister("enemy_0000abcd");
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void Sb13WarnsOnEveryStepOnSurfacesWithoutClips()
        {
            var footsteps = Add<FootstepAudio>();
            footsteps.Play("Grass");   // has a clip: silent
            footsteps.Play("Wood");    // has a clip: silent
            LogAssert.Expect(LogType.Warning, "Footstep audio clip missing for surface 'Gravel'");
            LogAssert.Expect(LogType.Warning, "Footstep audio clip missing for surface 'Gravel'");
            LogAssert.Expect(LogType.Warning, "Footstep audio clip missing for surface 'Metal'");
            footsteps.Play("Gravel");
            footsteps.Play("Gravel");  // the bug: spam on every step, not once per surface
            footsteps.Play("Metal");
        }
    }
}
