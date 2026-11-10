#nullable enable
using System.Collections.Generic;
using LabEscape.Core;
using UnityEngine;
using Grid = LabEscape.Core.Grid;

namespace LabEscape.Client
{
    /// <summary>A group of coloured parts that can flash and fade together.</summary>
    public sealed class Model
    {
        private readonly struct Part
        {
            public readonly MeshRenderer Renderer;
            public readonly Color Colour;

            public Part(MeshRenderer renderer, Color colour)
            {
                Renderer = renderer;
                Colour = colour;
            }
        }

        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        private readonly List<Part> _parts = new List<Part>();
        private readonly List<Transform> _bodyParts = new List<Transform>();
        private float _alpha = -1f;
        private float _flash = -1f;

        public readonly GameObject Root;

        /// <summary>The part of the model that moves on its own: it leans, squashes, hops and falls.</summary>
        public readonly Transform Body;

        /// <summary>The base plate, which stays flat on the floor under the body and says whose side a unit is on.</summary>
        public readonly Transform Plate;

        /// <summary>The body's pieces, which fly apart when a machine or an object breaks.</summary>
        public IReadOnlyList<Transform> BodyParts => _bodyParts;

        public Model(string name, Transform parent)
        {
            Root = new GameObject(name);
            Root.transform.SetParent(parent, false);
            var plate = new GameObject("plate");
            plate.transform.SetParent(Root.transform, false);
            Plate = plate.transform;
            var body = new GameObject("body");
            body.transform.SetParent(Root.transform, false);
            Body = body.transform;
        }

        public void Add(Transform parent, Mesh mesh, Vector3 pos, Vector3 size, Color colour, Vector3 euler = default)
        {
            var go = new GameObject(mesh.name, typeof(MeshFilter), typeof(MeshRenderer));
            var t = go.transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            t.localScale = size;
            t.localRotation = Quaternion.Euler(euler);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mats.Solid;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _parts.Add(new Part(r, colour));
            if (parent == Body) _bodyParts.Add(t);
        }

        public void Paint(float alpha, float flash = 0f)
        {
            if (Mathf.Approximately(alpha, _alpha) && Mathf.Approximately(flash, _flash)) return;
            _alpha = alpha;
            _flash = flash;
            var material = alpha < 0.995f ? Mats.SolidFade : Mats.Solid;
            foreach (var p in _parts)
            {
                p.Renderer.sharedMaterial = material;
                Block.SetColor(Mats.ColorId, new Color(p.Colour.r, p.Colour.g, p.Colour.b, p.Colour.a * alpha));
                Block.SetFloat(Mats.FlashId, flash);
                p.Renderer.SetPropertyBlock(Block);
            }
        }

        public void Destroy() => Object.Destroy(Root);
    }

    /// <summary>The greybox models. Silhouettes differ by unit, and the base plate says whose side a unit is on, so nothing rests on colour alone.</summary>
    public static class Figures
    {
        private sealed class Kit
        {
            private readonly Model _m;
            private readonly Transform _into;

            public Kit(Model m, Transform into)
            {
                _m = m;
                _into = into;
            }

            private void Add(Mesh mesh, float x, float y, float z, float sx, float sy, float sz, Color c, Vector3 euler) => _m.Add(_into, mesh, new Vector3(x, y, z), new Vector3(sx, sy, sz), c, euler);

            public void Box(float x, float y, float z, float sx, float sy, float sz, Color c, Vector3 euler = default) => Add(Shapes.Box, x, y, z, sx, sy, sz, c, euler);
            public void Cyl(float x, float y, float z, float sx, float sy, float sz, Color c, Vector3 euler = default) => Add(Shapes.Cylinder, x, y, z, sx, sy, sz, c, euler);
            public void Oct(float x, float y, float z, float sx, float sy, float sz, Color c, Vector3 euler = default) => Add(Shapes.Octagon, x, y, z, sx, sy, sz, c, euler);
            public void Hex(float x, float y, float z, float sx, float sy, float sz, Color c, Vector3 euler = default) => Add(Shapes.Hexagon, x, y, z, sx, sy, sz, c, euler);
            public void Cone(float x, float y, float z, float sx, float sy, float sz, Color c, Vector3 euler = default) => Add(Shapes.Cone, x, y, z, sx, sy, sz, c, euler);
            public void Ball(float x, float y, float z, float sx, float sy, float sz, Color c) => Add(Shapes.Ball, x, y, z, sx, sy, sz, c, default);
        }

        public static bool IsSmall(string def) => def == "hatchling" || def == "spitter" || def == "burster";

        /// <summary>The base plate says whose side a unit is on: round for yours, a diamond for the lab's soldiers, a hexagon for the Warden.</summary>
        private static void BasePlate(Model m, string def, Team team, Color colour)
        {
            var kit = new Kit(m, m.Plate);
            var dark = Palette.Shade(colour, 0.28f);
            var k = IsSmall(def) ? 0.7f : 1f;
            if (def == "warden")
            {
                kit.Hex(0f, 0f, 0f, 0.84f, 0.03f, 0.84f, colour);
                kit.Hex(0f, 0f, 0f, 0.68f, 0.05f, 0.68f, dark);
            }
            else if (team == Team.Enemy)
            {
                // A square on the world's axes is a diamond from this camera.
                kit.Box(0f, 0f, 0f, 0.56f * k, 0.03f, 0.56f * k, colour);
                kit.Box(0f, 0f, 0f, 0.42f * k, 0.05f, 0.42f * k, dark);
            }
            else
            {
                kit.Cyl(0f, 0f, 0f, 0.68f * k, 0.03f, 0.68f * k, colour);
                kit.Cyl(0f, 0f, 0f, 0.52f * k, 0.05f, 0.52f * k, dark);
            }
        }

        public static Model Unit(string def, Team team, Transform parent)
        {
            var m = new Model(def, parent);
            var c = Palette.UnitColor(def, team);
            BasePlate(m, def, team, c);
            var light = Palette.Lighten(c, 0.35f);
            var dark = Palette.Shade(c, 0.45f);
            var steel = Palette.Steel;
            var b = new Kit(m, m.Body);
            switch (def)
            {
                case "player":
                    b.Box(0f, 0.04f, 0f, 0.24f, 0.2f, 0.16f, dark);
                    b.Cyl(0f, 0.24f, 0f, 0.32f, 0.26f, 0.26f, c);
                    b.Box(0f, 0.28f, -0.17f, 0.2f, 0.22f, 0.08f, steel);
                    b.Ball(0f, 0.5f, 0f, 0.22f, 0.22f, 0.22f, light);
                    b.Box(0.19f, 0.33f, 0.09f, 0.07f, 0.07f, 0.32f, steel);
                    break;
                case "guard":
                    b.Box(0f, 0.04f, 0f, 0.26f, 0.22f, 0.18f, dark);
                    b.Box(0f, 0.26f, 0f, 0.36f, 0.26f, 0.24f, c);
                    b.Oct(0f, 0.52f, 0f, 0.27f, 0.16f, 0.27f, dark);
                    b.Box(0f, 0.57f, 0.12f, 0.18f, 0.05f, 0.05f, light);
                    b.Box(0.21f, 0.36f, 0.12f, 0.06f, 0.06f, 0.42f, steel);
                    break;
                case "medic":
                    b.Box(0f, 0.04f, 0f, 0.2f, 0.2f, 0.15f, dark);
                    b.Cyl(0f, 0.24f, 0f, 0.28f, 0.28f, 0.24f, c);
                    b.Ball(0f, 0.52f, 0f, 0.21f, 0.21f, 0.21f, light);
                    b.Box(0f, 0.8f, 0f, 0.28f, 0.07f, 0.09f, Color.white);
                    b.Box(0f, 0.8f, 0f, 0.09f, 0.07f, 0.28f, Color.white);
                    b.Box(-0.2f, 0.26f, 0f, 0.1f, 0.18f, 0.2f, Color.white);
                    break;
                case "flamer":
                    b.Box(0f, 0.04f, 0f, 0.26f, 0.22f, 0.18f, dark);
                    b.Cyl(0f, 0.26f, 0f, 0.34f, 0.26f, 0.28f, c);
                    b.Cyl(0f, 0.2f, -0.24f, 0.2f, 0.42f, 0.2f, Palette.Danger);
                    b.Oct(0f, 0.52f, 0f, 0.25f, 0.18f, 0.25f, steel);
                    b.Box(0.2f, 0.33f, 0.18f, 0.08f, 0.08f, 0.42f, steel);
                    b.Ball(0.2f, 0.32f, 0.4f, 0.1f, 0.1f, 0.1f, Palette.Hazard);
                    break;
                case "warden":
                    b.Box(-0.15f, 0.04f, 0f, 0.17f, 0.26f, 0.24f, dark);
                    b.Box(0.15f, 0.04f, 0f, 0.17f, 0.26f, 0.24f, dark);
                    b.Box(0f, 0.3f, 0f, 0.52f, 0.42f, 0.36f, c);
                    b.Box(-0.36f, 0.5f, 0f, 0.2f, 0.2f, 0.3f, dark);
                    b.Box(0.36f, 0.5f, 0f, 0.2f, 0.2f, 0.3f, dark);
                    b.Box(-0.36f, 0.22f, 0.04f, 0.15f, 0.28f, 0.18f, steel);
                    b.Box(0.36f, 0.22f, 0.04f, 0.15f, 0.28f, 0.18f, steel);
                    b.Box(0f, 0.72f, 0f, 0.26f, 0.2f, 0.26f, dark);
                    b.Box(0f, 0.79f, 0.13f, 0.2f, 0.06f, 0.04f, Palette.Hazard);
                    break;
                case "turret":
                    b.Cone(0f, 0.04f, 0f, 0.46f, 0.26f, 0.46f, steel);
                    b.Cyl(0f, 0.26f, 0f, 0.28f, 0.18f, 0.28f, c);
                    b.Box(0f, 0.32f, 0.2f, 0.08f, 0.08f, 0.4f, steel);
                    b.Box(0f, 0.46f, -0.02f, 0.14f, 0.06f, 0.14f, light);
                    break;
                case "drone":
                    b.Cyl(0f, 0.36f, 0f, 0.42f, 0.07f, 0.42f, c);
                    b.Ball(0f, 0.3f, 0f, 0.2f, 0.2f, 0.2f, light);
                    b.Cyl(0.22f, 0.4f, 0f, 0.14f, 0.05f, 0.14f, steel);
                    b.Cyl(-0.22f, 0.4f, 0f, 0.14f, 0.05f, 0.14f, steel);
                    b.Cyl(0f, 0.4f, 0.22f, 0.14f, 0.05f, 0.14f, steel);
                    b.Cyl(0f, 0.4f, -0.22f, 0.14f, 0.05f, 0.14f, steel);
                    break;
                case "hatchling":
                    b.Ball(0f, 0.04f, 0f, 0.3f, 0.2f, 0.38f, c);
                    b.Cone(0f, 0.18f, -0.06f, 0.08f, 0.16f, 0.08f, dark);
                    b.Cone(-0.08f, 0.16f, 0.08f, 0.07f, 0.13f, 0.07f, dark);
                    b.Cone(0.08f, 0.16f, 0.08f, 0.07f, 0.13f, 0.07f, dark);
                    break;
                case "spitter":
                    b.Ball(0f, 0.04f, -0.06f, 0.3f, 0.22f, 0.36f, c);
                    b.Cyl(0f, 0.18f, 0.06f, 0.1f, 0.24f, 0.1f, dark);
                    b.Ball(0f, 0.36f, 0.1f, 0.2f, 0.16f, 0.22f, Palette.Lighten(Palette.Corrode, 0.15f));
                    break;
                case "burster":
                    b.Ball(0f, 0.04f, 0f, 0.46f, 0.42f, 0.46f, c);
                    b.Ball(0.14f, 0.28f, 0.14f, 0.12f, 0.12f, 0.12f, light);
                    b.Ball(-0.15f, 0.22f, 0.08f, 0.1f, 0.1f, 0.1f, light);
                    b.Ball(0.02f, 0.36f, -0.12f, 0.1f, 0.1f, 0.1f, light);
                    break;
                default:
                    b.Cyl(0f, 0.04f, 0f, 0.34f, 0.5f, 0.34f, c);
                    b.Ball(0f, 0.54f, 0f, 0.22f, 0.22f, 0.22f, light);
                    break;
            }
            return m;
        }

        public static Model Object(BoardObject o, Transform parent)
        {
            var m = new Model(Names.Of(o.Kind), parent);
            var b = new Kit(m, m.Body);
            switch (o.Kind)
            {
                case ObjectKind.Mine:
                    b.Cyl(0f, 0f, 0f, 0.42f, 0.05f, 0.42f, Palette.Player);
                    b.Cyl(0f, 0f, 0f, 0.34f, 0.08f, 0.34f, Palette.Steel);
                    b.Cyl(0f, 0.08f, 0f, 0.12f, 0.03f, 0.12f, Palette.Danger);
                    break;
                case ObjectKind.Egg:
                    b.Cyl(0f, 0f, 0f, 0.4f, 0.03f, 0.4f, Palette.Shade(Palette.Hatch, 0.4f));
                    b.Ball(0f, 0.02f, 0f, 0.34f, 0.46f, 0.34f, Palette.Lighten(Palette.Hatch, 0.4f));
                    b.Ball(0.06f, 0.3f, 0.1f, 0.1f, 0.1f, 0.1f, Palette.Hatch);
                    break;
                case ObjectKind.Shield:
                    b.Box(0f, 0f, 0f, 0.8f, 0.05f, 0.8f, Palette.Shade(Palette.Player, 0.5f));
                    b.Box(0f, 0.04f, 0f, 0.62f, 0.56f, 0.62f, Palette.WithAlpha(Palette.Player, 0.75f));
                    b.Box(0f, 0.6f, 0f, 0.7f, 0.05f, 0.7f, Palette.Lighten(Palette.Player, 0.45f));
                    break;
                default:
                {
                    var of = Palette.UnitColor(o.Of ?? "", Team.Enemy);
                    b.Cyl(0f, 0f, 0f, 0.62f, 0.03f, 0.5f, Palette.Shade(of, 0.3f));
                    b.Ball(-0.08f, 0.01f, 0.02f, 0.36f, 0.1f, 0.24f, Palette.Shade(of, 0.65f));
                    b.Box(0.15f, 0.02f, -0.04f, 0.2f, 0.06f, 0.12f, Palette.Shade(of, 0.5f), new Vector3(0f, 30f, 0f));
                    break;
                }
            }
            return m;
        }

        /// <summary>The block for a terrain tile that stands up from the floor, and how tall it is.</summary>
        public static Model Block(string terrain, Pos? gunDir, Transform parent, out float height, out float halfWidth)
        {
            var m = new Model(terrain, parent);
            var b = new Kit(m, m.Body);
            switch (terrain)
            {
                case "pillar":
                    height = 0.9f;
                    halfWidth = 0.3f;
                    b.Oct(0f, 0f, 0f, 0.74f, 0.08f, 0.74f, Palette.WallFace);
                    b.Oct(0f, 0.08f, 0f, 0.58f, 0.74f, 0.58f, Palette.PillarFace);
                    b.Oct(0f, 0.82f, 0f, 0.68f, 0.08f, 0.68f, Palette.PillarTop);
                    break;
                case "shutter":
                    height = 0.72f;
                    halfWidth = 0.5f;
                    b.Box(-0.44f, 0f, 0f, 0.1f, 0.72f, 0.92f, Palette.WallFace);
                    b.Box(0.44f, 0f, 0f, 0.1f, 0.72f, 0.92f, Palette.WallFace);
                    for (var i = 0; i < 4; i++)
                    {
                        b.Box(0f, 0.08f + i * 0.17f, 0f, 0.84f, 0.09f, 0.36f, i % 2 == 0 ? Palette.Paint : Palette.Steel);
                    }
                    break;
                case "gun":
                {
                    height = 0.36f;
                    halfWidth = 0.42f;
                    var d = gunDir ?? Grid.W;
                    var yaw = Mathf.Atan2(d.X, -d.Y) * Mathf.Rad2Deg;
                    b.Box(0f, 0f, 0f, 0.86f, 0.24f, 0.86f, Palette.WallFace);
                    b.Oct(0f, 0.24f, 0f, 0.5f, 0.12f, 0.5f, Palette.Copper);
                    b.Box(d.X * 0.3f, 0.28f, -d.Y * 0.3f, 0.12f, 0.1f, 0.62f, Palette.Steel, new Vector3(0f, yaw, 0f));
                    break;
                }
                default:
                    height = 0.76f;
                    halfWidth = 0.5f;
                    b.Box(0f, 0f, 0f, 0.98f, 0.72f, 0.98f, Palette.WallFace);
                    b.Box(0f, 0.72f, 0f, 0.98f, 0.04f, 0.98f, Palette.WallTop);
                    break;
            }
            return m;
        }
    }
}
