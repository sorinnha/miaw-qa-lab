// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
using System;
using System.Collections.Generic;
using MiawWorks.QALab;

namespace QALab.Sandbox
{
    /// <summary>
    /// Ground truth for the sandbox's seeded bugs (spec 01, "Seeded bug catalog"). LabelRecorder
    /// writes these into labels.json for benchmark runs; <c>qalab eval</c> scores triage against them.
    /// Each feature must be an exact H2 heading in docs/sandbox_design.md (checked by a test).
    /// M1 has the log seeds; M4 adds SB06–SB12, SB15 and SB16.
    /// </summary>
    public static class SandboxSeedCatalog
    {
        /// <summary>Ids whose entry is still a YOU WRITE stub (left out of <see cref="All"/>).</summary>
        public static IReadOnlyList<string> Stubbed()
        {
            BuildAll(out var stubbed);
            return stubbed;
        }

        /// <summary>Every finished entry, in id order.</summary>
        public static IReadOnlyList<SeedCatalogEntry> All() => BuildAll(out _);

        private static List<SeedCatalogEntry> BuildAll(out List<string> stubbed)
        {
            var entries = new List<SeedCatalogEntry>();
            stubbed = new List<string>();
            var factories = new (string Id, Func<SeedCatalogEntry> Make)[]
            {
                ("SB01", SB01), ("SB02", SB02), ("SB03", SB03), ("SB04", SB04),
                ("SB05", SB05), ("SB13", SB13), ("SB14", SB14),
            };
            foreach (var (id, make) in factories)
            {
                try
                {
                    entries.Add(make());
                }
                catch (NotImplementedException)
                {
                    stubbed.Add(id);   // a YOU WRITE entry that isn't written yet
                }
            }
            return entries;
        }

        public static SeedCatalogEntry SB01() => Log(
            "SB01", "Doors", "S2",
            "Door_02 throws NullReferenceException when opened (hinge not assigned)",
            new MatchRule { StackContains = "SeededDoor.Open" });

        /// <summary>
        /// YOU WRITE (M1, with <c>SeededInventory.GetSlot</c>): the catalog entry for SB02.
        /// Feature "Inventory", severity S3, title "Inventory HUD requests slots beyond inventory size".
        /// The match rule is a <c>stack_contains</c> on the frame Unity prints for a Debug.LogError
        /// call inside <c>SeededInventory.GetSlot</c>. Look at a Debug.Log-style frame in
        /// docs/specs/00_contracts.md: it uses ':' between class and method, not '.'.
        /// </summary>
        public static SeedCatalogEntry SB02() => throw new NotImplementedException("YOU WRITE");

        public static SeedCatalogEntry SB03() => Log(
            "SB03", "Enemy registry", "S2",
            "Enemy registry lookup fails for unregistered enemy ids",
            new MatchRule { StackContains = "SeededEnemyRegistry.Get" });

        public static SeedCatalogEntry SB04() => Log(
            "SB04", "Spawner", "S2",
            "Spawner throws NullReferenceException when spawning a wave",
            new MatchRule { StackContains = "SeededSpawner.SpawnWave" });

        public static SeedCatalogEntry SB05() => Log(
            "SB05", "Asset loading", "S3",
            "Missing sound effect assets are requested by gameplay code",
            new MatchRule { MessageRegex = "^Failed to load asset Assets/Audio/sfx_" });

        public static SeedCatalogEntry SB13() => Log(
            "SB13", "Audio", "S4",
            "Footstep warning spam for surfaces without audio clips",
            new MatchRule { MessageRegex = "^Footstep audio clip missing" });

        public static SeedCatalogEntry SB14() => Log(
            "SB14", "Combat math", "S3",
            "SafeDivide called with zero divisor from damage and speed code",
            new MatchRule { StackContains = "MathUtil:SafeDivide" });

        private static SeedCatalogEntry Log(string id, string feature, string severity, string title, MatchRule match) =>
            new SeedCatalogEntry
            {
                BugId = id,
                Type = "log",
                Title = title,
                Feature = feature,
                ExpectedSeverity = severity,
                Match = match,
            };
    }
}
