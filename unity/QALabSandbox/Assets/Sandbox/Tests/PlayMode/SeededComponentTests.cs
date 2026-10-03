using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace QALab.Sandbox.Tests
{
    /// <summary>
    /// SB01 and SB04 build their state in Awake, which only runs in Play Mode. Both must throw a real
    /// NullReferenceException (the message triage expects), not Unity's editor-only
    /// UnassignedReference/MissingReference exceptions.
    /// </summary>
    public class SeededComponentTests
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
            foreach (var go in _objects) UnityEngine.Object.Destroy(go);
            _objects.Clear();
        }

        [Test]
        public void Sb01DoorWithoutHingeThrowsNullReference()
        {
            var door = Add<SeededDoor>();   // like Door_02: no hinge assigned
            var exception = Assert.Throws<NullReferenceException>(() => door.Open());
            Assert.AreEqual("Object reference not set to an instance of an object", exception.Message);
        }

        [Test]
        public void Sb04FirstWaveHitsTheRemovedSpawnPoint()
        {
            var spawner = Add<SeededSpawner>();   // default: one removed spawn point
            Assert.Throws<NullReferenceException>(() => spawner.SpawnWave());
        }

        [Test]
        public void Sb03AssigningATargetLooksUpAnUnregisteredId()
        {
            var spawner = Add<SeededSpawner>();
            Assert.Throws<KeyNotFoundException>(() => spawner.AssignTarget(new Enemy("enemy_00000001")));
        }
    }
}
