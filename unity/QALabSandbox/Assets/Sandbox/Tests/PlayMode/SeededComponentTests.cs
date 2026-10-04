using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace QALab.Sandbox.Tests
{
    /// <summary>
    /// SB01 and SB04 build their state in Awake, which only runs in Play Mode. Both must throw a real
    /// NullReferenceException (the message triage expects), not Unity's editor-only
    /// UnassignedReference/MissingReference exceptions. SB03 and SB04 share a script, so their stacks
    /// are checked against each other's catalog rules.
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

        /// <summary>
        /// The real stacks of both spawner seeds, thrown from Update as in a run: SB03's must contain
        /// its own catalog rule and not SB04's, so one exception never counts as two seeded bugs.
        /// </summary>
        [UnityTest]
        public IEnumerator Sb03AndSb04StacksFromUpdateMatchOnlyTheirOwnRules()
        {
            // One real spawn point plus the default removed one. Awake reads the serialized fields, so
            // set them (as the Inspector would) while the object is still inactive. The long interval
            // keeps timed waves out of the test: only the two requested waves run.
            var point = new GameObject("SpawnPoint_A");
            _objects.Add(point);
            var go = new GameObject("Spawner");
            _objects.Add(go);
            go.SetActive(false);
            var spawner = go.AddComponent<SeededSpawner>();
            SetField(spawner, "spawnPoints", new[] { point.transform });
            SetField(spawner, "waveIntervalS", 1e6f);
            go.SetActive(true);

            var exceptions = new List<(string Message, string Stack)>();
            void Capture(string message, string stack, LogType type)
            {
                if (type == LogType.Exception) exceptions.Add((message, stack));
            }
            Application.logMessageReceived += Capture;
            try
            {
                LogAssert.Expect(LogType.Exception, new Regex("^NullReferenceException"));
                LogAssert.Expect(LogType.Exception, new Regex("^KeyNotFoundException"));
                spawner.RequestWave();   // wave 1 (odd): the removed spawn point → SB04
                spawner.RequestWave();   // wave 2 (even): spawns, then a target lookup fails → SB03
                for (var i = 0; i < 5; i++) yield return null;
            }
            finally
            {
                Application.logMessageReceived -= Capture;
            }

            var sb03Rule = SandboxSeedCatalog.SB03().Match.StackContains;
            var sb04Rule = SandboxSeedCatalog.SB04().Match.StackContains;
            Assert.AreEqual(2, exceptions.Count);
            StringAssert.StartsWith("NullReferenceException", exceptions[0].Message);
            StringAssert.Contains(sb04Rule, exceptions[0].Stack);
            StringAssert.DoesNotContain(sb03Rule, exceptions[0].Stack);
            StringAssert.StartsWith("KeyNotFoundException", exceptions[1].Message);
            StringAssert.Contains(sb03Rule, exceptions[1].Stack);
            StringAssert.DoesNotContain(sb04Rule, exceptions[1].Stack, "SB03 would also match SB04's rule");
        }

        private static void SetField(object target, string name, object value) =>
            target.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(target, value);
    }
}
