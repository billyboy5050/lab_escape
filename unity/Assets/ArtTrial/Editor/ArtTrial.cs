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
        private const float PersonScale = 0.45f;

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
                People.Clear();
                Controllers.Clear();
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
                // Each set of units is shot whole and in two close-ups: the player's corner, where the Intruder, the drone, the
                // turret and the hatchling stand, and the first wave.
                void ShootSet(string name)
                {
                    Frame(cam, min, max);
                    Shoot(cam, Path.Combine(outDir, $"{name}.png"));
                    Frame(cam, new Vector3(-0.2f, -0.1f, -8.2f), new Vector3(4.6f, 1f, -4.6f));
                    Shoot(cam, Path.Combine(outDir, $"{name}-close.png"));
                    Frame(cam, new Vector3(0.6f, -0.1f, -4.2f), new Vector3(8.2f, 1.2f, 0.2f));
                    Shoot(cam, Path.Combine(outDir, $"{name}-guards.png"));
                }
                List<GameObject> Generated(Transform parent) => pieces.Select(p =>
                {
                    var m = Figures.Unit(p.Def, p.Team, parent, true);
                    m.Paint(1f);
                    m.Root.transform.position = Iso.Tile(p.Tile.X, p.Tile.Y);
                    m.Body.rotation = Facing(p, player);
                    return m.Root;
                }).ToList();
                var figures = pieces.Select(p =>
                {
                    var m = Figures.Unit(p.Def, p.Team, grey.transform, false);
                    m.Root.transform.position = Iso.Tile(p.Tile.X, p.Tile.Y);
                    m.Body.rotation = Facing(p, player);
                    m.Paint(1f);
                    return m.Root;
                }).ToList();
                ShootSet("greybox");

                // The greybox room with the generated models (UnitModels) in place of its figures.
                foreach (var f in figures) UnityEngine.Object.DestroyImmediate(f);
                Generated(grey.transform);
                ShootSet("generated");
                UnityEngine.Object.DestroyImmediate(grey);

                // The same room and units from the packs.
                var art = new GameObject("Art");
                BuildRoom(room, art.transform);
                var artUnits = new GameObject("Units").transform;
                artUnits.SetParent(art.transform, false);
                foreach (var p in pieces) BuildUnit(p, player, artUnits);
                Lights(w, h, art.transform);
                ShootSet("quaternius");

                // The free bodies as they come, in their own skin and underwear.
                foreach (var (go, body, tone) in People) Materials.Skin(go, body, tone);
                Frame(cam, new Vector3(1.6f, -0.1f, -4.2f), new Vector3(6.4f, 1.2f, -1.6f));
                Shoot(cam, Path.Combine(outDir, "quaternius-bare.png"));

                // The scene the trial's web build carries is saved here, before the generated models replace the packs' units.
                EditorSceneManager.SaveScene(scene, ScenePath);

                // The packs' room with the generated models in it.
                UnityEngine.Object.DestroyImmediate(artUnits.gameObject);
                Generated(art.transform);
                ShootSet("mixed");
                Debug.Log($"[ArtTrial] Captured into {outDir}");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }

        internal static string OutDir()
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

            // The two far walls, as the greybox has them: the kit's wall faces along the board's back edges, a band with a top on it.
            for (var x = 0; x < board.Width; x++)
            {
                var band = Wall(MegaKit("WallBand_Straight"), parent, new Vector3(x + 0.5f, 0f, 0f), Vector3.back);
                Wall(MegaKit("TopSimple_Straight"), parent, new Vector3(x + 0.5f, BoundsOf(band).max.y, 0f), Vector3.back);
            }
            for (var y = 0; y < board.Height; y++)
            {
                var band = Wall(MegaKit("WallBand_Straight"), parent, new Vector3(0f, 0f, -(y + 0.5f)), Vector3.right);
                Wall(MegaKit("TopSimple_Straight"), parent, new Vector3(0f, BoundsOf(band).max.y, -(y + 0.5f)), Vector3.right);
            }
        }

        /// <summary>
        /// Stands a wall piece on `foot`, just outside the board's edge, with its face toward `facing`. The kit's pivots and facings
        /// vary, so the piece is turned by its mesh's mean normal and moved by its bounds.
        /// </summary>
        private static GameObject Wall(string path, Transform parent, Vector3 foot, Vector3 facing)
        {
            var go = Place(path, parent, foot, Kit);
            var n = Vector3.zero;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                foreach (var v in mf.sharedMesh.normals) n += mf.transform.TransformDirection(v);
            }
            n.y = 0f;
            if (n.sqrMagnitude > 1e-6f)
            {
                go.transform.rotation = Quaternion.AngleAxis(Vector3.SignedAngle(n, facing, Vector3.up), Vector3.up) * go.transform.rotation;
            }
            var b = BoundsOf(go);
            var depth = Mathf.Abs(Vector3.Dot(b.size, facing));
            var shift = foot - b.center - facing * (depth * 0.5f);
            shift.y = foot.y - b.min.y;
            go.transform.position += shift;
            return go;
        }

        private static Bounds BoundsOf(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
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
                    Person(p, "Superhero_Male_FullBody", "Light", "Hair_SimpleParted", "Pistol_Aim_Neutral", at, face, parent);
                    break;
                case "guard":
                    Person(p, "Superhero_Male_FullBody", "Dark", "Hair_Buzzed", "Pistol_Idle_Loop", at, face, parent);
                    break;
                case "medic":
                    Person(p, "Superhero_Female_FullBody", "Dark", "Hair_SimpleParted", "Idle_Loop", at, face, parent);
                    break;
                case "drone":
                    Machine(Place(Essentials("Enemy_EyeDrone"), parent, at + Vector3.up * 0.5f, 0.42f, face));
                    break;
                case "turret":
                    Machine(Place(Essentials("Enemy_QuadShell"), parent, at, 0.3f, face));
                    break;
                case "hatchling":
                    Machine(Place(Essentials("Enemy_Trilobite"), parent, at, 0.18f, face));
                    break;
            }
        }

        /// <summary>One of the kit's creatures, posed in its idle, with an animator holding all its own clips.</summary>
        private static void Machine(GameObject go)
        {
            Pose(go, "Idle", 0.3f);
            var path = AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(go));
            Animate(go, Path.GetFileNameWithoutExtension(path), Clips(path));
        }

        /// <summary>The clips a person would play in a fight, so the web build carries what a game would.</summary>
        private static readonly string[] PersonClips =
        {
            "Idle_Loop", "Walk_Loop", "Jog_Fwd_Loop", "Pistol_Idle_Loop", "Pistol_Aim_Neutral", "Pistol_Shoot", "Pistol_Reload",
            "Hit_Chest", "Hit_Head", "Death01", "Spell_Simple_Shoot", "Interact", "Roll",
        };

        private static readonly Dictionary<string, UnityEditor.Animations.AnimatorController> Controllers =
            new Dictionary<string, UnityEditor.Animations.AnimatorController>();

        private static AnimationClip[] Clips(string modelPath) =>
            AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview")).ToArray();

        /// <summary>Gives a model an animator whose controller holds `clips`, made once per capture among the packs.</summary>
        private static void Animate(GameObject go, string name, AnimationClip[] clips)
        {
            if (!Controllers.TryGetValue(name, out var controller))
            {
                var path = $"{Packs}/Materials/{name}.controller";
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                AssetDatabase.DeleteAsset(path);
                controller = UnityEditor.Animations.AnimatorController.CreateAnimatorControllerAtPath(path);
                foreach (var clip in clips) controller.AddMotion(clip);
                Controllers[name] = controller;
            }
            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
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

        /// <summary>The people built, so the last shot can show them in the free bodies' own skin.</summary>
        private static readonly List<(GameObject Go, string Body, string Tone)> People = new List<(GameObject, string, string)>();

        /// <summary>
        /// A person: the free tier's base body in a suit of the unit's team colour, since the free tier has no clothes, with hair and
        /// a pose from the animation library.
        /// </summary>
        private static void Person(Piece p, string body, string skin, string hair, string clip, Vector3 at, float yaw, Transform parent)
        {
            var go = Place($"{Packs}/Characters/{body}.fbx", parent, at, PersonScale, yaw);
            Materials.Suit(go, body, Palette.UnitColor(p.Def, p.Team));
            Wear(go, $"{Packs}/Characters/{hair}.fbx");
            Pose(go, clip, 0.4f);
            if (clip.StartsWith("Pistol")) Arm(go, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, parent);
            var library = Clips($"{Packs}/Animations/UAL1_Standard.fbx");
            Animate(go, "Person", PersonClips.Select(n => library.FirstOrDefault(c => c.name.EndsWith("|" + n))).OfType<AnimationClip>().ToArray());
            People.Add((go, body, skin));
        }

        /// <summary>Puts the kit's pistol in the posed right hand, its barrel along the way the person faces.</summary>
        private static void Arm(GameObject body, Vector3 facing, Transform parent)
        {
            var hand = body.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "hand_r");
            if (hand == null)
            {
                Debug.LogWarning($"[ArtTrial] No hand_r on {body.name}");
                return;
            }
            // The gun's barrel runs along its model's -x axis.
            var gun = Place(Essentials("Gun_Pistol"), parent, hand.position, PersonScale);
            gun.transform.rotation = Quaternion.LookRotation(facing, Vector3.up) * Quaternion.Euler(0f, 90f, 0f);
            gun.transform.SetParent(hand, true);
        }

        /// <summary>Puts a part made for the same rig, such as hair, on a body by binding its meshes to the body's bones.</summary>
        private static void Wear(GameObject body, string path)
        {
            var bones = body.GetComponentsInChildren<Transform>(true).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null) throw new FileNotFoundException("Missing model", path);
            var part = (GameObject)PrefabUtility.InstantiatePrefab(asset, body.transform);
            part.transform.localPosition = Vector3.zero;
            part.transform.localRotation = Quaternion.identity;
            part.transform.localScale = Vector3.one;
            Materials.Fix(part);
            foreach (var smr in part.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                smr.bones = smr.bones.Select(b => b != null && bones.TryGetValue(b.name, out var t) ? t : b).ToArray();
                if (smr.rootBone != null && bones.TryGetValue(smr.rootBone.name, out var root)) smr.rootBone = root;
            }
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
            key.intensity = 0.9f;
            key.shadows = LightShadows.Soft;
            key.shadowStrength = 0.6f;
            // A teal wash from the back corner, as the greybox's lighting has.
            AddPoint("Teal glow", new Vector3(0.6f, 1.4f, -0.6f), new Color(0.35f, 0.85f, 0.8f), 5f, 0.6f, parent);
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

        internal static Camera MakeCamera()
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
        internal static void Frame(Camera cam, Vector3 min, Vector3 max)
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

        internal static void Shoot(Camera cam, string path)
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
