#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LabEscape.Client;
using LabEscape.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Grid = LabEscape.Core.Grid;

namespace LabEscape.ArtTrial.Editor
{
    /// <summary>
    /// The art trial: the Armory and its first wave built from Quaternius's CC0 packs, shot through the game's camera beside the
    /// greybox. Run from the menu, or in batch mode (not -nographics, since it renders) with
    /// -executeMethod LabEscape.ArtTrial.Editor.ArtTrial.Capture -artTrialOut &lt;folder&gt;.
    /// </summary>
    public static class ArtTrial
    {
        /// <summary>Where the packs are copied, ignored by git (docs/work/chore-art-trial-quaternius.md says where to get them).</summary>
        public const string Packs = "Assets/ArtTrial/Packs";

        /// <summary>The trial's scene, saved among the packs so the web build can include it.</summary>
        public const string ScenePath = Packs + "/ArtTrial.unity";

        /// <summary>The MegaKit is built on 4 m tiles, and a board tile is one unit.</summary>
        private const float Kit = 0.25f;

        /// <summary>People and machines a little larger than the kit's scale, as tactics games draw them, so they read on a tile.</summary>
        private const float PersonScale = 0.36f;

        private const int Width = 1920;
        private const int Height = 1200;

        // The game's camera (IsoCamera).
        private const float Pitch = 30f;
        private const float Yaw = -45f;

        private sealed class Piece
        {
            public string Def = "";
            public Team Team;
            public Pos Tile;
        }

        [MenuItem("Lab Escape/Art Trial/Capture")]
        public static void Capture()
        {
            try
            {
                var outDir = OutDir();
                Directory.CreateDirectory(outDir);
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var c = GameContent.Load();
                var room = c.DefaultRoom;
                var board = room.Board;
                var player = Tiles.Parse(room.PlayerStart);
                var pieces = new List<Piece> { new Piece { Def = "player", Team = Team.Player, Tile = player } };
                pieces.AddRange(room.Waves[0].Units.Select(u => new Piece { Def = u.Def, Team = Team.Enemy, Tile = Tiles.Parse(u.Tile) }));
                // Minions and a mine near the player, to see the kit's machines and a creature at the board's scale.
                pieces.Add(new Piece { Def = "drone", Team = Team.Player, Tile = Tiles.Parse("D7") });
                pieces.Add(new Piece { Def = "turret", Team = Team.Player, Tile = Tiles.Parse("A6") });
                pieces.Add(new Piece { Def = "hatchling", Team = Team.Player, Tile = Tiles.Parse("C8") });

                var cam = MakeCamera();
                float w = board.Width, h = board.Height;
                var min = new Vector3(-0.3f, -0.3f, -(h + 0.6f));
                var max = new Vector3(w + 0.6f, 1.3f, 0.3f);

                // The greybox, as the game draws it.
                Lighting.Reset();
                Lighting.SetRoom(new Vector3(w * 0.5f, 0f, -h * 0.5f), Mathf.Sqrt(w * w + h * h) * 0.5f);
                var grey = new GameObject("Greybox");
                new RoomView(room, grey.transform);
                foreach (var p in pieces)
                {
                    var m = Figures.Unit(p.Def, p.Team, grey.transform);
                    m.Root.transform.position = Iso.Tile(p.Tile.X, p.Tile.Y);
                    m.Body.rotation = Facing(p, player);
                    m.Paint(1f);
                }
                Frame(cam, min, max);
                Shoot(cam, Path.Combine(outDir, "greybox.png"));
                UnityEngine.Object.DestroyImmediate(grey);

                // The same room and units from the packs.
                var art = new GameObject("Art");
                BuildRoom(room, art.transform);
                foreach (var p in pieces) BuildUnit(p, player, art.transform);
                Lights(w, h, art.transform);
                Shoot(cam, Path.Combine(outDir, "quaternius.png"));

                // Close: the player's corner, where the Intruder, the drone, the turret and the hatchling stand.
                Frame(cam, new Vector3(-0.2f, -0.1f, -8.2f), new Vector3(4.6f, 1f, -4.6f));
                Shoot(cam, Path.Combine(outDir, "quaternius-close.png"));
                Frame(cam, new Vector3(0.6f, -0.1f, -4.2f), new Vector3(8.2f, 1.2f, 0.2f));
                Shoot(cam, Path.Combine(outDir, "quaternius-guards.png"));

                EditorSceneManager.SaveScene(scene, ScenePath);
                Debug.Log($"[ArtTrial] Captured into {outDir}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        private static string OutDir()
        {
            var args = Environment.GetCommandLineArgs();
            var i = Array.IndexOf(args, "-artTrialOut");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.GetFullPath("Builds/ArtTrial");
        }

        /// <summary>Turns a unit to face across the board: the lab's units toward the player, the player's side toward the first wave.</summary>
        private static Quaternion Facing(Piece p, Pos player)
        {
            var target = p.Team == Team.Enemy ? Iso.Tile(player.X, player.Y) : Iso.Tile(4, 2);
            var dir = target - Iso.Tile(p.Tile.X, p.Tile.Y);
            dir.y = 0f;
            return dir.sqrMagnitude < 0.01f ? Quaternion.identity : Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        // ---- The room ----

        private static GameObject Place(string path, Transform parent, Vector3 at, float scale, float yaw = 0f)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new FileNotFoundException("Missing model", path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            go.transform.localScale = go.transform.localScale * scale;
            Materials.Fix(go);
            return go;
        }

        private static string MegaKit(string name) => $"{Packs}/MegaKit/{name}.fbx";
        private static string Essentials(string name) => $"{Packs}/Essentials/{name}.fbx";

        private static void BuildRoom(RoomDef room, Transform parent)
        {
            var board = room.Board;
            float w = board.Width, h = board.Height;
            // A dark slab under the floor, as the greybox stands on, so the room's edge has depth.
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(slab.GetComponent<Collider>());
            slab.name = "Slab";
            slab.transform.SetParent(parent, false);
            slab.transform.position = new Vector3(w * 0.5f, -0.16f, -h * 0.5f);
            slab.transform.localScale = new Vector3(w + 0.3f, 0.3f, h + 0.3f);
            slab.GetComponent<Renderer>().sharedMaterial = Materials.Plain(new Color(0.06f, 0.07f, 0.08f));

            foreach (var t in board.AllTiles())
            {
                var id = board.TerrainId(t);
                if (id == "void") continue;
                var at = Iso.Tile(t.X, t.Y);
                var floor = id == "panel" ? "Platform_CenterPlate" : id == "vent" ? "Platform_DarkPlates" : (t.X + t.Y) % 2 == 0 ? "Platform_Simple" : "Platform_Squares";
                Place(MegaKit(floor), parent, at, Kit);
                switch (id)
                {
                    case "vent":
                        Place(MegaKit("Prop_Vent_Big"), parent, at + Vector3.up * 0.03f, 0.4f);
                        break;
                    case "wall":
                        // A wall tile stands alone in the Armory, so it is a stack of armoury crates; the kit's walls are faces for a room's edge.
                        Place(MegaKit("Prop_Crate3"), parent, at + Vector3.up * 0.4f, 0.8f);
                        break;
                    case "pillar":
                        Place(MegaKit("Column_Pipes"), parent, at, Kit);
                        break;
                    case "shutter":
                        Place(MegaKit("Door_Frame_Square_Blocked"), parent, at, 0.2f);
                        break;
                    case "gun":
                    {
                        var hz = room.Hazards.First(x => x.Tiles.Contains(Tiles.Name(t)));
                        var d = Grid.DirByName(hz.Direction!);
                        var yaw = Mathf.Atan2(d.X, -d.Y) * Mathf.Rad2Deg;
                        Place(MegaKit("Prop_Crate3"), parent, at + Vector3.up * 0.2f, 0.4f);
                        Place(Essentials("Gun_Rifle"), parent, at + Vector3.up * 0.48f, 0.55f, yaw + 90f);
                        break;
                    }
                }
            }

            // The two far walls: the kit's wall faces along the board's back edges, lower band and top.
            for (var x = 0; x < board.Width; x++)
            {
                Place(MegaKit("WallBand_Straight"), parent, Iso.Tile(x, 0), Kit, 90f);
                Place(MegaKit("TopSimple_Straight"), parent, Iso.Tile(x, 0), Kit, 90f);
            }
            for (var y = 0; y < board.Height; y++)
            {
                Place(MegaKit("WallBand_Straight"), parent, Iso.Tile(0, y), Kit);
                Place(MegaKit("TopSimple_Straight"), parent, Iso.Tile(0, y), Kit);
            }
            Place(MegaKit("Prop_Computer"), parent, new Vector3(5.5f, 0f, -0.15f), Kit, 0f);
        }

        // ---- The units ----

        private static void BuildUnit(Piece p, Pos player, Transform parent)
        {
            var at = Iso.Tile(p.Tile.X, p.Tile.Y);
            var face = Facing(p, player).eulerAngles.y;
            Plate(p, at, parent);
            switch (p.Def)
            {
                case "player":
                    Person(p, "Superhero_Male_FullBody", "Light", "Pistol_Aim_Neutral", at, face, parent);
                    break;
                case "guard":
                    Person(p, "Superhero_Male_FullBody", "Dark", "Pistol_Idle_Loop", at, face, parent);
                    break;
                case "medic":
                    Person(p, "Superhero_Female_FullBody", "Dark", "Idle_Loop", at, face, parent);
                    break;
                case "drone":
                    Pose(Place(Essentials("Enemy_EyeDrone"), parent, at + Vector3.up * 0.5f, 0.42f, face), "Idle", 0.3f);
                    break;
                case "turret":
                    Pose(Place(Essentials("Enemy_QuadShell"), parent, at, 0.3f, face), "Idle", 0.3f);
                    break;
                case "hatchling":
                    Pose(Place(Essentials("Enemy_Trilobite"), parent, at, 0.25f, face), "Idle", 0.3f);
                    break;
            }
        }

        /// <summary>The base plate the game puts under every unit: round for the player's side, a diamond (a square here) for the lab's.</summary>
        private static void Plate(Piece p, Vector3 at, Transform parent)
        {
            var go = GameObject.CreatePrimitive(p.Team == Team.Enemy ? PrimitiveType.Cube : PrimitiveType.Cylinder);
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.name = "Plate " + p.Def;
            go.transform.SetParent(parent, false);
            go.transform.position = at + Vector3.up * 0.012f;
            var s = Figures.IsSmall(p.Def) ? 0.5f : 0.66f;
            go.transform.localScale = p.Team == Team.Enemy ? new Vector3(s * 0.85f, 0.02f, s * 0.85f) : new Vector3(s, 0.01f, s);
            go.GetComponent<Renderer>().sharedMaterial = Materials.Plain(Palette.UnitColor(p.Def, p.Team), 0.6f);
        }

        private static void Person(Piece p, string body, string skin, string clip, Vector3 at, float yaw, Transform parent)
        {
            var go = Place($"{Packs}/Characters/{body}.fbx", parent, at, PersonScale, yaw);
            Materials.Skin(go, body, skin);
            Pose(go, clip, 0.4f);
        }

        /// <summary>Puts a model in a pose: a clip of the Universal Animation Library for people, the model's own clip for the rest.</summary>
        private static void Pose(GameObject go, string clipName, float time)
        {
            var source = AssetDatabase.LoadAllAssetsAtPath($"{Packs}/Animations/UAL1_Standard.fbx").OfType<AnimationClip>()
                .Concat(AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go))).OfType<AnimationClip>())
                .FirstOrDefault(c => c.name == clipName || c.name.EndsWith("|" + clipName));
            if (source == null)
            {
                Debug.LogWarning($"[ArtTrial] No clip {clipName} for {go.name}");
                return;
            }
            source.SampleAnimation(go, Mathf.Min(time, source.length));
        }

        // ---- Light and camera ----

        private static void Lights(float w, float h, Transform parent)
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.32f, 0.4f, 0.44f);
            RenderSettings.ambientEquatorColor = new Color(0.2f, 0.23f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.08f, 0.09f);
            var key = new GameObject("Key light").AddComponent<Light>();
            key.transform.SetParent(parent, false);
            key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(52f, 150f, 0f);
            key.color = new Color(1f, 0.95f, 0.88f);
            key.intensity = 1.1f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.7f;
            AddPoint("Teal glow", new Vector3(1.2f, 1.6f, -1.2f), new Color(0.35f, 0.85f, 0.8f), 6f, 1.2f, parent);
            AddPoint("Room lamp", new Vector3(w * 0.5f, 3f, -h * 0.5f), new Color(1f, 0.9f, 0.75f), 9f, 0.9f, parent);
        }

        private static void AddPoint(string name, Vector3 at, Color colour, float range, float intensity, Transform parent)
        {
            var l = new GameObject(name).AddComponent<Light>();
            l.transform.SetParent(parent, false);
            l.transform.position = at;
            l.type = LightType.Point;
            l.color = colour;
            l.range = range;
            l.intensity = intensity;
        }

        private static Camera MakeCamera()
        {
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Palette.Background;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            cam.allowHDR = false;
            cam.transform.rotation = Quaternion.Euler(Pitch, Yaw, 0f);
            return cam;
        }

        /// <summary>Keeps a world box in view, as the game's camera frames the room.</summary>
        private static void Frame(Camera cam, Vector3 min, Vector3 max)
        {
            var t = cam.transform;
            float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
            for (var i = 0; i < 8; i++)
            {
                var p = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z);
                float r = Vector3.Dot(p, t.right), u = Vector3.Dot(p, t.up);
                minR = Mathf.Min(minR, r);
                maxR = Mathf.Max(maxR, r);
                minU = Mathf.Min(minU, u);
                maxU = Mathf.Max(maxU, u);
            }
            var aspect = Width / (float)Height;
            cam.aspect = aspect;
            cam.orthographicSize = Mathf.Max((maxU - minU) * 0.5f, (maxR - minR) * 0.5f / aspect) * 1.04f;
            var centre = (min + max) * 0.5f;
            var onScreen = t.right * ((minR + maxR) * 0.5f) + t.up * ((minU + maxU) * 0.5f);
            var depth = Vector3.Dot(centre, t.forward);
            t.position = onScreen + t.forward * (depth - 60f);
        }

        private static void Shoot(Camera cam, string path)
        {
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            var was = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply();
            RenderTexture.active = was;
            cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
