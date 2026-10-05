using System.Collections.Generic;
using System.IO;
using MiawWorks.QALab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if QALAB_AI_NAVIGATION
using Unity.AI.Navigation;
#endif

namespace QALab.Sandbox.Editor
{
    /// <summary>
    /// Tools > QA Lab > Rebuild Sandbox Scenes. Builds Sandbox_Level01 and Sandbox_Menu from code, bakes
    /// the NavMesh and saves both scenes, so the level is reproducible and reviewable: change this file,
    /// rebuild, and commit the regenerated .unity files with it (never edit the scenes by hand).
    /// Geometry and camera follow docs/sandbox_design.md (the RAG ground truth): 2 m tiles T_00–T_399,
    /// camera 6 m behind and 2.5 m above the player. Positions come from <see cref="SandboxLayout"/>.
    /// Contents: floor tiles with footstep surfaces, outer walls, 3 doors (Door_02 without hinge), crates
    /// (Crate_07 loses its material, SB09), the spawner, the player, the inventory HUD, the asset loader,
    /// combat math and the F1 menu; the south-east corridor and room over T_17 (SB06), the north-west room
    /// behind the narrow gap (SB07), GcZone (SB08), the camera zone (SB10), the score label (SB11), the
    /// ammo icon (SB12) and the ballistics range (SB16).
    /// </summary>
    public static class SandboxSceneBuilder
    {
        private const string Root = "Assets/Sandbox";
        private const string ScenesFolder = Root + "/Scenes";
        private const string MaterialsFolder = Root + "/Materials";
        public const string LevelScene = ScenesFolder + "/Sandbox_Level01.unity";
        public const string MenuScene = ScenesFolder + "/Sandbox_Menu.unity";
        // docs/sandbox_design.md, "Level geometry": a 40 × 40 m grid of 2 m tiles, T_00 to T_399.
        private const float TileSize = SandboxLayout.TileSize;
        private const int TilesPerSide = SandboxLayout.TilesPerSide;
        private const float WallThickness = 0.3f;
        private const float WallHeight = 3f;
        public const string NavMeshAsset = ScenesFolder + "/NavMesh-Sandbox_Level01.asset";

        [MenuItem("Tools/QA Lab/Rebuild Sandbox Scenes", priority = 20)]
        public static void RebuildAll()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Directory.CreateDirectory(ScenesFolder);
            Directory.CreateDirectory(MaterialsFolder);
            BuildLevel();
            BuildMenu();
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(LevelScene, true),
                new EditorBuildSettingsScene(MenuScene, true),
            };
            EnsureSettingsAsset();
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(LevelScene);
            Debug.Log("[QALab] rebuilt " + LevelScene + " and " + MenuScene);
        }

        // ---- Sandbox_Level01 ----------------------------------------------------------------------

        private static void BuildLevel()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AddLight();

            var level = new GameObject("Level");
            var floor = Child(level, "Floor");
            for (var x = 0; x < TilesPerSide; x++)
            {
                for (var z = 0; z < TilesPerSide; z++)
                {
                    AddTile(floor, x, z);
                }
            }
            var walls = Child(level, "Walls");
            AddWalls(walls);
            AddSouthEastRoom(walls);
            AddNorthWestRoom(walls);
            var doors = Child(level, "Doors");
            AddDoor(doors, "Door_01", new Vector3(4f, 1f, 10f), withHinge: true);
            var brokenDoor = AddDoor(doors, "Door_02", new Vector3(8f, 1f, 5.5f), withHinge: false);   // SB01
            AddDoor(doors, "Door_03", new Vector3(16f, 1f, 12f), withHinge: true);
            var crates = Child(level, "Crates");
            for (var i = 1; i <= 7; i++)
            {
                var crate = Box(crates, $"Crate_{i:00}", new Vector3(22f + (i % 4) * 2.5f, 0.5f, 24f + (i / 4) * 3f), Vector3.one, MaterialFor("Crate", new Color(0.55f, 0.4f, 0.25f)));
                crate.isStatic = true;
                if (i == 7) crate.AddComponent<SeededMissingMaterial>();   // SB09: magenta at runtime
            }
            var launcher = AddBallisticsRange(level);

            var player = AddPlayer();
            AddCamera(player.transform);
            var spawner = AddSpawner(level);
            var hud = AddHud();
            var score = AddScoreLabel(hud.gameObject);
            AddAmmoPanel(hud.gameObject, launcher);
            var zones = new GameObject("Zones");
            var gcZone = AddZone(zones, "GcZone", SandboxLayout.GcZoneX0, SandboxLayout.GcZoneX1, SandboxLayout.GcZoneZ0,
                SandboxLayout.GcZoneZ1, new Color(0.75f, 0.35f, 0.3f)).AddComponent<GcZone>();
            var cameraZone = AddZone(zones, "CameraZone", SandboxLayout.CameraZoneX0, SandboxLayout.CameraZoneX1,
                SandboxLayout.CameraZoneZ0, SandboxLayout.CameraZoneZ1, new Color(0.3f, 0.35f, 0.75f)).AddComponent<CameraZone>();

            var systems = new GameObject("Systems");
            var assetLoader = systems.AddComponent<AssetLoader>();
            var combat = systems.AddComponent<CombatDriver>();
            var menu = systems.AddComponent<DebugSeedMenu>();
            Wire(menu, "interactor", player.GetComponent<Interactor>());
            Wire(menu, "brokenDoor", brokenDoor);
            Wire(menu, "hud", hud);
            Wire(menu, "spawner", spawner);
            Wire(menu, "assetLoader", assetLoader);
            Wire(menu, "player", player.GetComponent<SandboxPlayer>());
            Wire(menu, "combat", combat);
            Wire(menu, "gcZone", gcZone);
            Wire(menu, "cameraZone", cameraZone);
            Wire(menu, "score", score);
            Wire(menu, "launcher", launcher);

            BakeNavMesh(level);
            EditorSceneManager.SaveScene(scene, LevelScene);
        }

        /// <summary>
        /// Tile index = row × 20 + column, written with at least two digits: T_00 is the tile at the
        /// origin corner (x 0–2 m, z 0–2 m), T_17 is row 0, column 17 (x 34–36 m), T_399 the far corner.
        /// Each tile is tagged with its footstep surface.
        /// </summary>
        private static void AddTile(GameObject parent, int x, int z)
        {
            var surface = SurfaceFor(x, z);
            var index = z * TilesPerSide + x;
            var tile = Box(parent, $"T_{index:00}", new Vector3(x * TileSize + TileSize / 2f, -0.25f, z * TileSize + TileSize / 2f),
                new Vector3(TileSize, 0.5f, TileSize), MaterialFor(surface, SurfaceColor(surface)));
            tile.isStatic = true;
            tile.AddComponent<SurfaceTag>().Set(surface);
            if (index == SandboxLayout.HoleTile)
            {
                tile.AddComponent<SeededMissingCollider>();   // SB06: the collider goes at runtime, the renderer stays
            }
        }

        /// <summary>
        /// A gravel path at x 4–8 m, a metal deck beyond x, z ≥ 24 m, wood at z 4–8 m, grass elsewhere.
        /// (Column/row = metres / 2.) Gravel and Metal have no footstep clip (SB13).
        /// </summary>
        private static string SurfaceFor(int x, int z)
        {
            if (x >= 2 && x <= 3) return "Gravel";
            if (x >= 12 && z >= 12) return "Metal";
            if (z >= 2 && z <= 3) return "Wood";
            return "Grass";
        }

        private static Color SurfaceColor(string surface)
        {
            switch (surface)
            {
                case "Gravel": return new Color(0.55f, 0.55f, 0.52f);
                case "Metal": return new Color(0.45f, 0.5f, 0.58f);
                case "Wood": return new Color(0.6f, 0.45f, 0.3f);
                default: return new Color(0.35f, 0.6f, 0.3f);
            }
        }

        private static void AddWalls(GameObject parent)
        {
            var size = TileSize * TilesPerSide;
            var mat = MaterialFor("Wall", new Color(0.75f, 0.75f, 0.78f));
            Box(parent, "Wall_South", new Vector3(size / 2f, 1.5f, -0.25f), new Vector3(size, 3f, 0.5f), mat).isStatic = true;
            Box(parent, "Wall_North", new Vector3(size / 2f, 1.5f, size + 0.25f), new Vector3(size, 3f, 0.5f), mat).isStatic = true;
            Box(parent, "Wall_West", new Vector3(-0.25f, 1.5f, size / 2f), new Vector3(0.5f, 3f, size), mat).isStatic = true;
            Box(parent, "Wall_East", new Vector3(size + 0.25f, 1.5f, size / 2f), new Vector3(0.5f, 3f, size), mat).isStatic = true;
        }

        /// <summary>
        /// SB06: the south-east room (x 30–40, z 2–10) is entered only through the corridor along the south
        /// wall (z 0–2) and a 2 m doorway at its south-east corner, so every path into it crosses T_17.
        /// </summary>
        private static void AddSouthEastRoom(GameObject parent)
        {
            var mat = MaterialFor("Wall", new Color(0.75f, 0.75f, 0.78f));
            const float x0 = SandboxLayout.SouthEastX0, z1 = SandboxLayout.CorridorZ1, top = SandboxLayout.SouthEastRoomZ1;
            const float size = SandboxLayout.LevelSize;
            Wall(parent, "Room_SE_South", x0, SandboxLayout.SouthEastDoorX0, z1, z1 + WallThickness, mat);   // corridor's north side
            Wall(parent, "Room_SE_West", x0, x0 + WallThickness, z1, top + WallThickness, mat);
            Wall(parent, "Room_SE_North", x0, size, top, top + WallThickness, mat);
        }

        /// <summary>
        /// SB07: the north-west room (x 0–10, z 30–40) has one 1.6 m doorway in its south wall. A post
        /// (SeededNarrowGap) narrows it to 0.76 m; the NavMesh is baked with the post in place.
        /// </summary>
        private static void AddNorthWestRoom(GameObject parent)
        {
            var mat = MaterialFor("Wall", new Color(0.75f, 0.75f, 0.78f));
            const float x1 = SandboxLayout.NorthWestRoomX1, z0 = SandboxLayout.NorthWestRoomZ0;
            const float size = SandboxLayout.LevelSize, half = WallThickness / 2f;
            Wall(parent, "Room_NW_South_A", 0f, SandboxLayout.GapOpeningX0, z0 - half, z0 + half, mat);
            Wall(parent, "Room_NW_South_B", SandboxLayout.GapOpeningX1, x1 + WallThickness, z0 - half, z0 + half, mat);
            Wall(parent, "Room_NW_East", x1, x1 + WallThickness, z0 - half, size, mat);
            var post = Wall(parent, "GapPost_SB07", SandboxLayout.GapOpeningX0, SandboxLayout.GapX0, z0 - half, z0 + half,
                MaterialFor("Post", new Color(0.6f, 0.6f, 0.65f)));
            post.AddComponent<SeededNarrowGap>();
        }

        /// <summary>A static wall box covering x0–x1, z0–z1, from the floor to WallHeight.</summary>
        private static GameObject Wall(GameObject parent, string name, float x0, float x1, float z0, float z1, Material mat)
        {
            var wall = Box(parent, name, new Vector3((x0 + x1) / 2f, WallHeight / 2f, (z0 + z1) / 2f),
                new Vector3(x1 - x0, WallHeight, z1 - z0), mat);
            wall.isStatic = true;
            return wall;
        }

        /// <summary>
        /// SB16: the launcher fires east along z = 35 at a 5 cm wall. The wall is part of the level (the
        /// NavMesh walks around it); the launcher is not (the bot walks under the firing line).
        /// </summary>
        private static ProjectileLauncher AddBallisticsRange(GameObject level)
        {
            var range = new GameObject("BallisticsRange");
            var wallMat = MaterialFor("RangeWall", new Color(0.85f, 0.8f, 0.5f));
            var thin = Box(Child(level, "Range"), "ThinWall",
                new Vector3(SandboxLayout.ThinWallX + SandboxLayout.ThinWallThicknessM / 2f, SandboxLayout.ThinWallHeightM / 2f,
                    (SandboxLayout.ThinWallZ0 + SandboxLayout.ThinWallZ1) / 2f),
                new Vector3(SandboxLayout.ThinWallThicknessM, SandboxLayout.ThinWallHeightM, SandboxLayout.ThinWallZ1 - SandboxLayout.ThinWallZ0),
                wallMat);
            thin.isStatic = true;
            var launcher = Box(range, "ProjectileLauncher", new Vector3(SandboxLayout.LauncherX, SandboxLayout.LauncherY, SandboxLayout.LauncherZ),
                new Vector3(0.3f, 0.3f, 0.6f), MaterialFor("Launcher", new Color(0.25f, 0.25f, 0.28f)));
            launcher.transform.rotation = Quaternion.Euler(0f, 90f, 0f);   // forward = +x, towards the wall
            return launcher.AddComponent<ProjectileLauncher>();
        }

        /// <summary>A floor zone: an empty object at its centre plus a flat coloured marker (no collider).</summary>
        private static GameObject AddZone(GameObject parent, string name, float x0, float x1, float z0, float z1, Color color)
        {
            var zone = Child(parent, name);
            zone.transform.position = new Vector3((x0 + x1) / 2f, 0f, (z0 + z1) / 2f);
            var marker = Box(zone, name + "_Marker", new Vector3((x0 + x1) / 2f, 0.005f, (z0 + z1) / 2f),
                new Vector3(x1 - x0, 0.01f, z1 - z0), MaterialFor(name, color));
            Object.DestroyImmediate(marker.GetComponent<Collider>());
            return zone;
        }

        /// <summary>A door panel; with a hinge it swings around the hinge child, without it SB01 fires.</summary>
        private static GameObject AddDoor(GameObject parent, string name, Vector3 position, bool withHinge)
        {
            var door = new GameObject(name);
            door.transform.SetParent(parent.transform, false);
            door.transform.position = position;
            var panelParent = door.transform;
            Transform hinge = null;
            if (withHinge)
            {
                hinge = new GameObject("Hinge").transform;
                hinge.SetParent(door.transform, false);
                hinge.localPosition = new Vector3(-0.5f, 0f, 0f);
                panelParent = hinge;
            }
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Panel";
            panel.transform.SetParent(panelParent, false);
            panel.transform.localScale = new Vector3(1f, 2f, 0.15f);
            panel.transform.localPosition = withHinge ? new Vector3(0.5f, 0f, 0f) : Vector3.zero;
            panel.GetComponent<Renderer>().sharedMaterial = MaterialFor("Door", new Color(0.45f, 0.3f, 0.2f));
            var seeded = door.AddComponent<SeededDoor>();
            Wire(seeded, "hinge", hinge);   // Door_02 stays unassigned on purpose
            return door;
        }

        private static GameObject AddPlayer()
        {
            var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            player.name = "Player";
            Object.DestroyImmediate(player.GetComponent<CapsuleCollider>());   // the CharacterController collides
            player.transform.position = new Vector3(2f, 1.1f, 2f);
            var controller = player.AddComponent<CharacterController>();
            controller.radius = 0.4f;
            controller.height = 2f;
            var interactor = player.AddComponent<Interactor>();
            var footsteps = player.AddComponent<FootstepAudio>();
            var sandboxPlayer = player.AddComponent<SandboxPlayer>();
            Wire(sandboxPlayer, "interactor", interactor);
            Wire(sandboxPlayer, "footsteps", footsteps);
            player.GetComponent<Renderer>().sharedMaterial = MaterialFor("Player", new Color(0.2f, 0.45f, 0.9f));
            return player;
        }

        private static void AddCamera(Transform player)
        {
            var go = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = go.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            go.AddComponent<AudioListener>();
            go.transform.position = player.position + new Vector3(0f, 2.5f, -6f);   // design doc: "Camera"
            go.transform.LookAt(player.position + Vector3.up);
            var follow = go.AddComponent<FollowCamera>();
            follow.SetTarget(player);
        }

        private static SeededSpawner AddSpawner(GameObject level)
        {
            var spawnerGo = Child(level, "Spawner");
            spawnerGo.transform.position = new Vector3(20f, 0f, 9f);
            var points = new List<Transform>();
            var offsets = new[] { new Vector3(-3f, 0f, 2f), new Vector3(0f, 0f, 4f), new Vector3(3f, 0f, 2f) };
            for (var i = 0; i < offsets.Length; i++)
            {
                var point = Child(spawnerGo, $"SpawnPoint_{i + 1}");
                point.transform.localPosition = offsets[i];
                points.Add(point.transform);
            }
            var spawner = spawnerGo.AddComponent<SeededSpawner>();
            var so = new SerializedObject(spawner);
            var array = so.FindProperty("spawnPoints");
            array.arraySize = points.Count;
            for (var i = 0; i < points.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = points[i];
            }
            so.FindProperty("removedSpawnPoints").intValue = 1;   // SB04: a point deleted from the level
            so.ApplyModifiedPropertiesWithoutUndo();
            return spawner;
        }

        /// <summary>Screen-space canvas with the inventory panel (5 slots) and SeededInventory.</summary>
        private static HudInventory AddHud()
        {
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasGo.AddComponent<GraphicRaycaster>();

            var panel = UiRect(canvasGo, "InventoryPanel", new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(5 * 70f + 10f, 80f));
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);
            var slots = new List<Image>();
            for (var i = 0; i < SeededInventory.Size; i++)
            {
                var slot = UiRect(panel, $"Slot_{i}", new Vector2(0f, 0.5f), new Vector2(40f + i * 70f, 0f), new Vector2(60f, 60f));
                slots.Add(slot.AddComponent<Image>());
            }
            var inventory = canvasGo.AddComponent<SeededInventory>();
            var hud = canvasGo.AddComponent<HudInventory>();
            Wire(hud, "inventory", inventory);
            Wire(hud, "panel", panel);
            var so = new SerializedObject(hud);
            var array = so.FindProperty("slotImages");
            array.arraySize = slots.Count;
            for (var i = 0; i < slots.Count; i++)
            {
                array.GetArrayElementAtIndex(i).objectReferenceValue = slots[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            return hud;
        }

        /// <summary>
        /// Score label, top-left: a fixed 136 px box that fits "Score: 9999" but not "Score: 10000" at 24 px
        /// (SB11). The text may run past the box (horizontal overflow), so the overflow is visible.
        /// </summary>
        private static HudScore AddScoreLabel(GameObject canvas)
        {
            var box = UiRect(canvas, "ScoreBox", new Vector2(0f, 1f), new Vector2(88f, -30f), new Vector2(136f, 36f));
            box.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var textGo = UiRect(box, "ScoreLabel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(136f, 36f));
            var text = textGo.AddComponent<Text>();
            text.text = "Score: 0";
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.fontSize = 24;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var score = canvas.AddComponent<HudScore>();
            Wire(score, "label", text);
            return score;
        }

        /// <summary>Ammo icon and count, bottom-right, shown near the ballistics range (SB12 empties the icon).</summary>
        private static void AddAmmoPanel(GameObject canvas, ProjectileLauncher launcher)
        {
            var panel = UiRect(canvas, "AmmoPanel", new Vector2(1f, 0f), new Vector2(-90f, 60f), new Vector2(140f, 56f));
            panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.55f);
            var iconGo = UiRect(panel, "AmmoIcon", new Vector2(0f, 0.5f), new Vector2(32f, 0f), new Vector2(40f, 40f));
            var icon = iconGo.AddComponent<Image>();
            icon.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            icon.color = new Color(0.95f, 0.75f, 0.2f);
            var countGo = UiRect(panel, "AmmoCount", new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(70f, 40f));
            var count = countGo.AddComponent<Text>();
            count.text = "30";
            count.alignment = TextAnchor.MiddleRight;
            count.fontSize = 24;
            count.color = Color.white;
            count.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            panel.SetActive(false);
            var ammo = canvas.AddComponent<HudAmmo>();
            Wire(ammo, "panel", panel);
            Wire(ammo, "icon", icon);
            Wire(ammo, "count", count);
            Wire(ammo, "launcher", launcher);
        }

        private static void BakeNavMesh(GameObject level)
        {
#if QALAB_AI_NAVIGATION
            SetHumanoidAgentRadius(SandboxLayout.NavMeshAgentRadiusM);
            var surface = level.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.Children;
            // 5 cm voxels: SB07's gap leaves a 16 cm strip for a 0.3 m agent, too thin for the default
            // voxel size (radius / 3 = 10 cm) to keep it connected.
            surface.overrideVoxelSize = true;
            surface.voxelSize = 0.05f;
            // Render meshes, not physics colliders: a tile with a renderer but no collider (SB06, M4)
            // must still look walkable to the NavMesh, which is exactly how the bot falls through it.
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.RenderMeshes;
            surface.BuildNavMesh();
            // Save the baked data as its own asset; otherwise it is serialized into the .unity file
            // and every rebuild produces a huge, unreviewable scene diff.
            AssetDatabase.DeleteAsset(NavMeshAsset);
            AssetDatabase.CreateAsset(surface.navMeshData, NavMeshAsset);
#else
            Debug.LogWarning("[QALab] AI Navigation package missing: install com.unity.ai.navigation, then rebuild the scenes (the bot needs the NavMesh)");
#endif
        }

        /// <summary>
        /// The design doc's NavMesh agent radius (0.3 m) on the built-in Humanoid agent type, which
        /// NavMeshSurface bakes with. Agent types live in ProjectSettings/NavMeshAreas.asset; there is no
        /// public API to change them, so this edits the asset through SerializedObject.
        /// </summary>
        private static void SetHumanoidAgentRadius(float radius)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset");
            var settings = assets != null && assets.Length > 0 ? new SerializedObject(assets[0]).FindProperty("m_Settings") : null;
            if (settings == null || !settings.isArray)
            {
                Debug.LogError("[QALab] could not find the NavMesh agent settings: set the Humanoid agent radius to " + radius + " m by hand (Window > AI > Navigation > Agents)");
                return;
            }
            for (var i = 0; i < settings.arraySize; i++)
            {
                var agent = settings.GetArrayElementAtIndex(i);
                if (agent.FindPropertyRelative("agentTypeID").intValue != 0) continue;
                agent.FindPropertyRelative("agentRadius").floatValue = radius;
            }
            settings.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            var actual = UnityEngine.AI.NavMesh.GetSettingsByID(0).agentRadius;
            if (Mathf.Abs(actual - radius) > 0.001f)
            {
                Debug.LogError($"[QALab] Humanoid agent radius is {actual} m, expected {radius} m: SB07 won't behave as designed");
            }
        }

        // ---- Sandbox_Menu (the UI crawler's playground) -------------------------------------------

        private static void BuildMenu()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.12f, 0.16f);

            var canvasGo = new GameObject("MenuCanvas");
            canvasGo.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasGo.AddComponent<GraphicRaycaster>();
            AddEventSystem();

            var main = UiRect(canvasGo, "MainPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 360f));
            var settings = UiRect(canvasGo, "SettingsPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 240f));
            var credits = UiRect(canvasGo, "CreditsPanel", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 240f));
            settings.SetActive(false);
            credits.SetActive(false);

            var menu = canvasGo.AddComponent<SandboxMainMenu>();
            Wire(menu, "settings", canvasGo.AddComponent<SeededSettingsMenu>());   // SB15
            Wire(menu, "mainPanel", main);
            Wire(menu, "settingsPanel", settings);
            Wire(menu, "creditsPanel", credits);

            AddButton(main, "Play", 120f, menu, nameof(SandboxMainMenu.Play));
            AddButton(main, "Settings", 40f, menu, nameof(SandboxMainMenu.ShowSettings));
            AddButton(main, "Credits", -40f, menu, nameof(SandboxMainMenu.ShowCredits));
            AddButton(main, "Quit", -120f, menu, nameof(SandboxMainMenu.Quit));
            AddButton(settings, "Apply", 40f, menu, nameof(SandboxMainMenu.ApplySettings));
            AddButton(settings, "Back", -40f, menu, nameof(SandboxMainMenu.ShowMain));
            AddButton(credits, "Back", 0f, menu, nameof(SandboxMainMenu.ShowMain));
            EditorSceneManager.SaveScene(scene, MenuScene);
        }

        /// <summary>EventSystem with the input module that matches the active input backend.</summary>
        private static void AddEventSystem()
        {
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
#if QALAB_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<StandaloneInputModule>();
#endif
        }

        private static void AddButton(GameObject parent, string label, float y, SandboxMainMenu target, string method)
        {
            var go = UiRect(parent, label + "Button", new Vector2(0.5f, 0.5f), new Vector2(0f, y), new Vector2(240f, 56f));
            go.AddComponent<Image>().color = new Color(0.25f, 0.3f, 0.4f);
            var button = go.AddComponent<UnityEngine.UI.Button>();
            var textGo = UiRect(go, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(240f, 56f));
            var text = textGo.AddComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 24;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var action = (UnityEngine.Events.UnityAction)System.Delegate.CreateDelegate(typeof(UnityEngine.Events.UnityAction), target, method);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick, action);
        }

        // ---- helpers ------------------------------------------------------------------------------

        private static void AddLight()
        {
            var go = new GameObject("Directional Light");
            var light = go.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static GameObject Child(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            return go;
        }

        private static GameObject Box(GameObject parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent.transform, false);
            go.transform.position = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject UiRect(GameObject parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return go;
        }

        /// <summary>A saved material asset (scenes can only reference assets), URP Lit when available.</summary>
        private static Material MaterialFor(string name, Color color)
        {
            var path = $"{MaterialsFolder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;   // maps to _BaseColor on URP Lit, _Color on Standard
            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>Set a private [SerializeField] through SerializedObject (no public setters needed).</summary>
        private static void Wire(Object owner, string field, Object value)
        {
            var so = new SerializedObject(owner);
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogError($"[QALab] {owner.GetType().Name} has no serialized field '{field}'");
                return;
            }
            property.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A settings asset for Play Mode: auto-start, manual adapter, benchmark, runs/ at the repo root.</summary>
        private static void EnsureSettingsAsset()
        {
            const string folder = "Assets/QALab/Resources";
            var path = $"{folder}/{QALabSettings.ResourceName}.asset";
            if (AssetDatabase.LoadAssetAtPath<QALabSettings>(path) != null) return;
            Directory.CreateDirectory(folder);
            var settings = ScriptableObject.CreateInstance<QALabSettings>();
            AssetDatabase.CreateAsset(settings, path);
            var so = new SerializedObject(settings);
            so.FindProperty("autoStartInPlayMode").boolValue = true;
            so.FindProperty("outDir").stringValue = "../../runs";
            so.FindProperty("adapter").stringValue = "manual";
            so.FindProperty("benchmark").boolValue = true;
            so.FindProperty("durationS").floatValue = 120f;
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log("[QALab] created " + path + " (auto-start on; runs go to <repo>/runs)");
        }
    }
}
