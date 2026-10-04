using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace QALab.Sandbox.Tests
{
    /// <summary>
    /// The M4 seeds do what the catalog says, outside a QA Lab run (where every seed is on): T_17 loses
    /// its collider, Crate_07 its material, Apply throws with nothing to apply, the camera zone blacks
    /// out and recovers, and a 120 m/s projectile with discrete collision passes a 5 cm wall while a
    /// continuous one stops.
    /// </summary>
    public class SeededLevelTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        private GameObject Track(GameObject go)
        {
            _objects.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects) if (go != null) UnityEngine.Object.Destroy(go);
            _objects.Clear();
            PlayerPrefs.DeleteKey("sandbox.volume");
            AudioListener.volume = 1f;   // SB15's successful Apply sets it
        }

        [Test]
        public void Sb06TileKeepsItsRendererButLosesItsCollider()
        {
            var tile = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            tile.AddComponent<SeededMissingCollider>();
            Assert.IsFalse(tile.GetComponent<Collider>().enabled);
            Assert.IsTrue(tile.GetComponent<Renderer>().enabled);
        }

        [Test]
        public void Sb09CrateLosesItsMaterial()
        {
            var crate = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            var seed = crate.AddComponent<SeededMissingMaterial>();
            Assert.IsNull(crate.GetComponent<Renderer>().sharedMaterial);
            Assert.AreEqual("missing_texture", seed.Label);
            Assert.IsFalse(seed.IsVisible(null), "no camera, nothing on screen");
        }

        [Test]
        public void Sb15ApplyThrowsOnlyWhenNothingChanged()
        {
            var menu = Track(new GameObject("Settings")).AddComponent<SeededSettingsMenu>();
            Assert.Throws<InvalidOperationException>(() => menu.Apply());
            menu.SetVolume(0.3f);
            Assert.DoesNotThrow(() => menu.Apply(), "a real change applies");
        }

        [UnityTest]
        public IEnumerator Sb10CameraZoneBlacksOutForTwoSecondsThenRestores()
        {
            var cameraGo = Track(new GameObject("Main Camera") { tag = "MainCamera" });
            var camera = cameraGo.AddComponent<Camera>();
            camera.cullingMask = ~0;
            var zone = Track(new GameObject("CameraZone")).AddComponent<CameraZone>();
            zone.RequestBlackout();
            yield return null;
            yield return null;
            Assert.IsTrue(zone.IsBlack);
            Assert.AreEqual(0, camera.cullingMask);
            Assert.IsTrue(zone.IsVisible(camera));
            yield return new WaitForSeconds(2.3f);
            Assert.IsFalse(zone.IsBlack);
            Assert.AreEqual(~0, camera.cullingMask, "the 3D view comes back");
        }

        [UnityTest]
        public IEnumerator Sb16DiscreteProjectileTunnelsThroughTheThinWall()
        {
            yield return Shoot(CollisionDetectionMode.Discrete, out var body);
            Assert.Greater(body.position.x, 5.1f, "120 m/s moves 2.4 m per step: the 5 cm wall is never touched");
        }

        [UnityTest]
        public IEnumerator ContinuousProjectileStopsAtTheThinWall()
        {
            yield return Shoot(CollisionDetectionMode.ContinuousDynamic, out var body);
            Assert.Less(body.position.x, 5.1f, "continuous collision sweeps the step and hits the wall");
        }

        private IEnumerator Shoot(CollisionDetectionMode mode, out Rigidbody body)
        {
            var wall = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            wall.transform.position = new Vector3(5f, 0f, 0f);
            wall.transform.localScale = new Vector3(0.05f, 3f, 3f);
            var projectile = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            projectile.transform.localScale = Vector3.one * 0.1f;
            projectile.transform.position = new Vector3(0.3f, 0f, 0f);
            body = projectile.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.collisionDetectionMode = mode;
#if UNITY_6000_0_OR_NEWER
            body.linearVelocity = new Vector3(120f, 0f, 0f);
#else
            body.velocity = new Vector3(120f, 0f, 0f);
#endif
            return Steps(6);
        }

        private static IEnumerator Steps(int count)
        {
            for (var i = 0; i < count; i++) yield return new WaitForFixedUpdate();
        }
    }
}
