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
    /// Positions come from <see cref="SandboxLayout"/>, the same numbers the scene builder uses.
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
                ("SB01", SB01), ("SB02", SB02), ("SB03", SB03), ("SB04", SB04), ("SB05", SB05),
                ("SB06", SB06), ("SB07", SB07), ("SB08", SB08), ("SB09", SB09), ("SB10", SB10),
                ("SB11", SB11), ("SB12", SB12), ("SB13", SB13), ("SB14", SB14), ("SB15", SB15),
                ("SB16", SB16),
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
        /// Learning task (M1, with <c>SeededInventory.GetSlot</c>), written by Claude at Sora's request
        /// (D-030): the catalog entry for SB02. The match rule is a <c>stack_contains</c> on the frame
        /// Unity prints for a Debug.LogError call inside <c>SeededInventory.GetSlot</c>. Debug.Log-style
        /// frames use ':' between class and method, not '.' (docs/specs/00_contracts.md).
        /// </summary>
        public static SeedCatalogEntry SB02() => Log(
            "SB02", "Inventory", "S3",
            "Inventory HUD requests slots beyond inventory size",
            new MatchRule { StackContains = "SeededInventory:GetSlot" });

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

        public static SeedCatalogEntry SB06() => Detector(
            "SB06", "Level geometry", "S2",
            "Player falls through floor tile T_17 (missing collider)",
            new MatchRule
            {
                Detector = "fell_out_of_world",
                Near = new[] { SandboxLayout.TileCenterX(SandboxLayout.HoleTile), 0f, SandboxLayout.TileCenterZ(SandboxLayout.HoleTile) },
                Radius = 4f,
            });

        public static SeedCatalogEntry SB07() => Detector(
            "SB07", "Level geometry", "S3",
            "Player gets stuck in a doorway gap the NavMesh treats as walkable",
            new MatchRule
            {
                Detector = "stuck",
                Near = new[] { SandboxLayout.GapCenterX, 0f, SandboxLayout.NorthWestRoomZ0 },
                Radius = 3f,
            });

        public static SeedCatalogEntry SB08() => Detector(
            "SB08", "Performance", "S3",
            "Frame-time spikes in GcZone from large garbage allocations",
            new MatchRule { Detector = "perf_spike" });

        public static SeedCatalogEntry SB09() => Visual(
            "SB09", "Art assets", "S3", "Crate_07 renders magenta (material missing)", "missing_texture");

        public static SeedCatalogEntry SB10() => Visual(
            "SB10", "Camera", "S2", "3D view goes black for 2 s in the camera trigger zone", "black_screen");

        public static SeedCatalogEntry SB11() => Visual(
            "SB11", "HUD", "S3", "Score label overflows its box from 10000 points", "ui_overflow");

        public static SeedCatalogEntry SB12() => Visual(
            "SB12", "HUD", "S3", "Ammo icon is a white box (image without a sprite)", "placeholder_ui");

        public static SeedCatalogEntry SB13() => Log(
            "SB13", "Audio", "S4",
            "Footstep warning spam for surfaces without audio clips",
            new MatchRule { MessageRegex = "^Footstep audio clip missing" });

        public static SeedCatalogEntry SB14() => Log(
            "SB14", "Combat math", "S3",
            "SafeDivide called with zero divisor from damage and speed code",
            new MatchRule { StackContains = "MathUtil:SafeDivide" });

        public static SeedCatalogEntry SB15() => Log(
            "SB15", "Settings menu", "S2",
            "Settings Apply throws InvalidOperationException when nothing was changed",
            new MatchRule { StackContains = "SeededSettingsMenu.Apply" });

        public static SeedCatalogEntry SB16() => Detector(
            "SB16", "Ballistics range", "S3",
            "Fast projectile passes through the 5 cm range wall (discrete collision)",
            new MatchRule { Detector = "tunneling" });

        private static SeedCatalogEntry Log(string id, string feature, string severity, string title, MatchRule match) =>
            Entry(id, "log", feature, severity, title, match);

        private static SeedCatalogEntry Detector(string id, string feature, string severity, string title, MatchRule match) =>
            Entry(id, "detector", feature, severity, title, match);

        private static SeedCatalogEntry Visual(string id, string feature, string severity, string title, string label) =>
            Entry(id, "visual", feature, severity, title, new MatchRule { VisualLabel = label });

        private static SeedCatalogEntry Entry(string id, string type, string feature, string severity, string title, MatchRule match) =>
            new SeedCatalogEntry
            {
                BugId = id,
                Type = type,
                Title = title,
                Feature = feature,
                ExpectedSeverity = severity,
                Match = match,
            };
    }
}
