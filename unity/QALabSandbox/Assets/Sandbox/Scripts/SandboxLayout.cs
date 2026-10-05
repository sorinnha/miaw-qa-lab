// Engine-free: compiled by Unity and by tools/cs-check (.NET). No UnityEngine here.
namespace QALab.Sandbox
{
    /// <summary>
    /// Where things are in Sandbox_Level01, in metres (x east, z north, y up; the floor top is y = 0).
    /// One source for the scene builder, the seed scripts and the seed catalog's <c>near</c> rules, so
    /// the ground truth can't drift from the geometry (D-022). Follows docs/sandbox_design.md.
    /// </summary>
    public static class SandboxLayout
    {
        // "Level geometry": a 40 × 40 m grid of 2 m tiles, T_00 to T_399, index = row × 20 + column.
        public const float TileSize = 2f;
        public const int TilesPerSide = 20;
        public const float LevelSize = TileSize * TilesPerSide;
        public const float PlayerRadiusM = 0.4f;
        /// <summary>The NavMesh agent radius from the design doc (the builder sets the Humanoid agent to it).</summary>
        public const float NavMeshAgentRadiusM = 0.3f;

        public static float TileCenterX(int index) => index % TilesPerSide * TileSize + TileSize / 2f;
        public static float TileCenterZ(int index) => index / TilesPerSide * TileSize + TileSize / 2f;

        // ---- SB06: tile T_17 (row 0, column 17: x 34–36, z 0–2) has a renderer but no collider. It lies in
        // the south-east corridor (z 0–2, x 30–40), the only way into the south-east room (x 30–40, z 2–10,
        // entered at its south-east corner, x 38–40), so every trip to that room walks over it.
        public const int HoleTile = 17;
        public const float SouthEastX0 = 30f;
        public const float CorridorZ1 = 2f;
        public const float SouthEastRoomZ1 = 10f;
        public const float SouthEastDoorX0 = 38f;

        // ---- SB07: the north-west room (x 0–10, z 30–40) is entered through one opening in its south
        // wall. A post narrows it to 0.76 m: wider than the NavMesh agent (2 × 0.3 m), so the bot plans
        // through it, but narrower than the player (2 × 0.4 m), so the player gets stuck.
        public const float NorthWestRoomX1 = 10f;
        public const float NorthWestRoomZ0 = 30f;
        public const float GapOpeningX0 = 4.6f;
        public const float GapOpeningX1 = 6.2f;
        public const float GapPostWidthM = 0.84f;
        public static float GapX0 => GapOpeningX0 + GapPostWidthM;
        public static float GapWidthM => GapOpeningX1 - GapX0;
        public static float GapCenterX => (GapX0 + GapOpeningX1) / 2f;

        // ---- SB08: GcZone in the middle of the level.
        public const float GcZoneX0 = 16f, GcZoneX1 = 24f, GcZoneZ0 = 16f, GcZoneZ1 = 24f;

        // ---- SB10: the camera trigger zone on the west side.
        public const float CameraZoneX0 = 2f, CameraZoneX1 = 8f, CameraZoneZ0 = 14f, CameraZoneZ1 = 20f;

        // ---- SB12 / SB16: the ballistics range in the north-east corner. The launcher fires east along
        // z = 35 at a 5 cm, 3 m high wall while the player is within 15 m, at 2.5 m: above the player's
        // head (2.1 m), so a bot walking through the range is never in the line of fire.
        public const float LauncherX = 31f, LauncherY = 2.5f, LauncherZ = 35f;
        public const float ThinWallX = 37.5f, ThinWallThicknessM = 0.05f, ThinWallHeightM = 3f, ThinWallZ0 = 33f, ThinWallZ1 = 37f;
        public const float RangeActiveRadiusM = 15f;

        public static bool Inside(float x, float z, float x0, float x1, float z0, float z1) =>
            x >= x0 && x <= x1 && z >= z0 && z <= z1;
    }
}
