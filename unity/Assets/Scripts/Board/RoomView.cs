#nullable enable
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;
using UnityEngine;
using Grid = LabEscape.Core.Grid;

namespace LabEscape.Client
{
    /// <summary>The room, built once from its data. Terrain that stands up is a separate block for each tile, so it can fade.</summary>
    public sealed class RoomView
    {
        private const float BackWall = 1.1f;
        private const float FadedAlpha = 0.22f;
        private const float Edge = 0.24f;

        /// <summary>Where the board's coordinates sit: this far out from the near edges of the tiles, at this height.</summary>
        public const float CoordOut = Edge + 0.2f;
        public const float CoordHeight = -0.3f;

        private sealed class Block
        {
            public Model Model = null!;
            public Pos Tile;
            public float Height;
            public float HalfWidth;
            public float Alpha = 1f;
        }

        private readonly RoomDef _room;
        private readonly List<Block> _blocks = new List<Block>();

        public readonly Vector3 BoundsMin;
        public readonly Vector3 BoundsMax;

        /// <summary>The middle of the floor and the distance from it to a far corner: where the room's light pools.</summary>
        public readonly Vector3 Centre;
        public readonly float Reach;

        public RoomView(RoomDef room, Transform parent)
        {
            _room = room;
            var board = room.Board;
            var gunDirs = new Dictionary<Pos, Pos>();
            foreach (var h in room.Hazards)
            {
                if (string.IsNullOrEmpty(h.Direction)) continue;
                foreach (var t in h.Tiles) gunDirs[Tiles.Parse(t)] = Grid.DirByName(h.Direction!);
            }

            var b = new MeshBuilder();
            float w = board.Width, h2 = board.Height;
            // The slab everything stands on, then the room's two far walls and its low front lip.
            var edge = Edge;
            b.Box(new Vector3(-edge, -0.34f, -(h2 + edge)), new Vector3(w + edge, -0.14f, edge), Palette.Frame);
            b.Box(new Vector3(-edge, -0.14f, 0f), new Vector3(w + edge, BackWall, edge), Palette.WallFace);
            b.Box(new Vector3(-edge, -0.14f, -h2), new Vector3(0f, BackWall, 0f), Palette.WallFace);
            b.Box(new Vector3(0f, 0.74f, -0.02f), new Vector3(w, 0.79f, 0.01f), Palette.Accent);
            b.Box(new Vector3(-0.01f, 0.74f, -h2), new Vector3(0.02f, 0.79f, 0f), Palette.Accent);
            b.Box(new Vector3(w, -0.14f, -(h2 + edge)), new Vector3(w + edge, 0.05f, 0f), Palette.FrameHi);
            b.Box(new Vector3(0f, -0.14f, -(h2 + edge)), new Vector3(w, 0.05f, -h2), Palette.FrameHi);
            // Hazard stripes along the two near lips, as the web room has.
            Stripes(b, new Vector3(0f, 0f, -(h2 + edge)), Vector3.right, w + edge, Vector3.back);
            Stripes(b, new Vector3(w + edge, 0f, 0f), Vector3.back, h2 + edge, Vector3.right);

            foreach (var t in board.AllTiles())
            {
                var id = board.TerrainId(t);
                if (id == "void") continue;
                var col = (t.X + t.Y) % 2 == 0 ? Palette.FloorA : Palette.FloorB;
                b.Box(Iso.Floor(t.X + 0.015f, t.Y + 0.985f, -0.14f), Iso.Floor(t.X + 0.985f, t.Y + 0.015f, 0f), col);
                if (id == "vent") Vent(b, t);
                else if (id == "panel") Panel(b, t);
                else if (IsBlock(id))
                {
                    var m = Figures.Block(id, gunDirs.TryGetValue(t, out var d) ? d : (Pos?)null, parent, out var height, out var half);
                    m.Root.transform.position = Iso.Tile(t.X, t.Y);
                    m.Paint(1f);
                    _blocks.Add(new Block { Model = m, Tile = t, Height = height, HalfWidth = half });
                }
            }

            AddMesh("Room", b, Mats.Solid, parent);

            Centre = new Vector3(w * 0.5f, 0f, -h2 * 0.5f);
            Reach = Mathf.Sqrt(w * w + h2 * h2) * 0.5f;
            var g = new MeshBuilder();
            Ground(g, w, h2, edge);
            ContactShade(g, board, w, h2);
            AddMesh("Ground and contact shade", g, Mats.Ground, parent);

            // The near sides leave room for the coordinates.
            BoundsMin = new Vector3(-edge, -0.3f, -(h2 + CoordOut + 0.2f));
            BoundsMax = new Vector3(w + CoordOut + 0.2f, BackWall, edge);
        }

        private static bool IsBlock(string id) => id == "wall" || id == "pillar" || id == "shutter" || id == "gun";

        private static void AddMesh(string name, MeshBuilder b, Material material, Transform parent)
        {
            var mesh = new Mesh { name = name, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            b.Apply(mesh);
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        /// <summary>
        /// Slanted stripes of paint along the outer face of a lip, which starts at `from` and runs `length` along `along`; `outward`
        /// is the way the face looks.
        /// </summary>
        private static void Stripes(MeshBuilder b, Vector3 from, Vector3 along, float length, Vector3 outward)
        {
            const float y0 = -0.1f, y1 = 0.02f, width = 0.13f, period = 0.26f, slant = 0.1f;
            Color32 paint = Palette.Shade(Palette.Paint, 0.8f);
            var o = from + outward * 0.004f;
            for (var s = 0f; s + width + slant <= length; s += period)
            {
                var p0 = o + along * s + Vector3.up * y0;
                var p1 = o + along * (s + width) + Vector3.up * y0;
                var p2 = o + along * (s + width + slant) + Vector3.up * y1;
                var p3 = o + along * (s + slant) + Vector3.up * y1;
                if (Vector3.Dot(Vector3.Cross(p1 - p0, p3 - p0), outward) > 0f) b.Quad(p0, p1, p2, p3, paint);
                else b.Quad(p0, p3, p2, p1, paint);
            }
        }

        /// <summary>
        /// What the room stands on, below the slab: a dark floor ruled in tiles that fades out into the dark, lit by the web
        /// client's teal glow from behind the room, with the slab's shadow on it.
        /// </summary>
        private void Ground(MeshBuilder g, float w, float h, float edge)
        {
            const float y = -0.5f, out_ = 13f;
            var centre = new Vector3(Centre.x, y, Centre.z);
            // The glow sits behind the room, toward its far corner, so it rises above the far walls as the web client's does.
            var glowAt = centre + new Vector3(-0.22f * w, 0f, 0.22f * h);
            g.Disc(glowAt, Reach * 2.3f, Vector3.right, Vector3.forward, new Color(0.16f, 0.36f, 0.36f, 0.3f), new Color(0.16f, 0.36f, 0.36f, 0f), 48);

            // Grid lines on whole tiles, fading with the distance from the room.
            var line = new Color(0.47f, 0.78f, 0.75f, 0.075f);
            float near = Reach + 1f, far = Reach + 9f;
            Color32 At(Vector3 p)
            {
                var d = Vector3.Distance(new Vector3(p.x, y, p.z), centre);
                return new Color(line.r, line.g, line.b, line.a * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(near, far, d))));
            }
            int x0 = Mathf.FloorToInt(-out_), x1 = Mathf.CeilToInt(w + out_), z0 = Mathf.FloorToInt(-h - out_), z1 = Mathf.CeilToInt(out_);
            const float half = 0.012f;
            for (var x = x0; x <= x1; x++)
            {
                for (var z = z0; z < z1; z++) Segment(g, new Vector3(x, y, z), new Vector3(x, y, z + 1), Vector3.right * half, At);
            }
            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x < x1; x++) Segment(g, new Vector3(x, y, z), new Vector3(x + 1, y, z), Vector3.forward * half, At);
            }

            // The slab's shadow, softest at its edge.
            Skirt(g, new Vector3(w * 0.5f, y + 0.001f, (edge - h - edge) * 0.5f), (w + 2f * edge) * 0.5f, (h + 2f * edge) * 0.5f, 1.1f, 0.6f);
        }

        private static void Segment(MeshBuilder g, Vector3 a, Vector3 b, Vector3 side, System.Func<Vector3, Color32> colour)
        {
            Color32 ca = colour(a), cb = colour(b);
            if (ca.a == 0 && cb.a == 0) return;
            var i = g.Vert(a - side, Vector3.up, ca);
            g.Vert(b - side, Vector3.up, cb);
            g.Vert(b + side, Vector3.up, cb);
            g.Vert(a + side, Vector3.up, ca);
            g.Tri(i, i + 1, i + 2);
            g.Tri(i, i + 2, i + 3);
        }

        /// <summary>
        /// Shade on the floor where something stands on it: at the foot of the far walls and around every block, darkest against
        /// it and gone a third of a tile away. It keeps the blocks from floating on the floor.
        /// </summary>
        private static void ContactShade(MeshBuilder g, Board board, float w, float h)
        {
            const float y = 0.004f, reach = 0.36f, dark = 0.5f;
            Color32 inner = new Color(0f, 0f, 0f, dark), outer = new Color(0f, 0f, 0f, 0f);
            Band(g, new Vector3(0f, y, 0f), new Vector3(w, y, 0f), new Vector3(0f, 0f, -reach), inner, outer);
            Band(g, new Vector3(0f, y, -h), new Vector3(0f, y, 0f), new Vector3(reach, 0f, 0f), inner, outer);
            foreach (var t in board.AllTiles())
            {
                var id = board.TerrainId(t);
                if (!IsBlock(id)) continue;
                var at = Iso.Tile(t.X, t.Y, y);
                if (id == "pillar") Halo(g, at, 0.36f, 0.36f + reach * 0.9f, inner, outer);
                else Skirt(g, at, id == "gun" ? 0.43f : 0.49f, id == "gun" ? 0.43f : 0.49f, reach, dark);
            }
        }

        /// <summary>A band on the floor from the line a–b, shading out along `across`.</summary>
        private static void Band(MeshBuilder g, Vector3 a, Vector3 b, Vector3 across, Color32 inner, Color32 outer)
        {
            var i = g.Vert(a, Vector3.up, inner);
            g.Vert(b, Vector3.up, inner);
            g.Vert(b + across, Vector3.up, outer);
            g.Vert(a + across, Vector3.up, outer);
            g.Tri(i, i + 1, i + 2);
            g.Tri(i, i + 2, i + 3);
        }

        /// <summary>A shade around a box's footprint on the floor, `spread` wide.</summary>
        private static void Skirt(MeshBuilder g, Vector3 centre, float halfX, float halfZ, float spread, float dark)
        {
            Color32 inner = new Color(0f, 0f, 0f, dark), outer = new Color(0f, 0f, 0f, 0f);
            var c = new[] { new Vector3(-halfX, 0f, -halfZ), new Vector3(halfX, 0f, -halfZ), new Vector3(halfX, 0f, halfZ), new Vector3(-halfX, 0f, halfZ) };
            for (var k = 0; k < 4; k++)
            {
                Vector3 a = c[k], b = c[(k + 1) % 4];
                // Out from each corner along its diagonal, so the sides meet in a mitre.
                var oa = new Vector3(Mathf.Sign(a.x), 0f, Mathf.Sign(a.z)) * spread;
                var ob = new Vector3(Mathf.Sign(b.x), 0f, Mathf.Sign(b.z)) * spread;
                var i = g.Vert(centre + a, Vector3.up, inner);
                g.Vert(centre + b, Vector3.up, inner);
                g.Vert(centre + b + ob, Vector3.up, outer);
                g.Vert(centre + a + oa, Vector3.up, outer);
                g.Tri(i, i + 1, i + 2);
                g.Tri(i, i + 2, i + 3);
            }
        }

        /// <summary>A round shade between two radii, dark at the inner one.</summary>
        private static void Halo(MeshBuilder g, Vector3 centre, float r0, float r1, Color32 inner, Color32 outer)
        {
            const int sides = 24;
            var first = -1;
            for (var i = 0; i < sides; i++)
            {
                var a = i * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var v = g.Vert(centre + dir * r0, Vector3.up, inner);
                g.Vert(centre + dir * r1, Vector3.up, outer);
                if (first < 0) first = v;
            }
            for (var i = 0; i < sides; i++)
            {
                int a = first + i * 2, b = a + 1, c = first + (i + 1) % sides * 2, d = c + 1;
                g.Tri(a, b, d);
                g.Tri(a, d, c);
            }
        }

        private static void Vent(MeshBuilder b, Pos t)
        {
            b.Box(Iso.Floor(t.X + 0.14f, t.Y + 0.86f, 0f), Iso.Floor(t.X + 0.86f, t.Y + 0.14f, 0.015f), Palette.Steel);
            for (var i = 0; i < 4; i++)
            {
                var y0 = t.Y + 0.22f + i * 0.16f;
                b.Box(Iso.Floor(t.X + 0.22f, y0 + 0.07f, 0.015f), Iso.Floor(t.X + 0.78f, y0, 0.03f), Palette.PillarTop);
            }
        }

        private static void Panel(MeshBuilder b, Pos t)
        {
            b.Box(Iso.Floor(t.X + 0.06f, t.Y + 0.94f, 0f), Iso.Floor(t.X + 0.94f, t.Y + 0.06f, 0.012f), Palette.Copper);
            b.Box(Iso.Floor(t.X + 0.16f, t.Y + 0.84f, 0f), Iso.Floor(t.X + 0.84f, t.Y + 0.16f, 0.022f), Palette.Steel);
            b.Box(Iso.Floor(t.X + 0.46f, t.Y + 0.78f, 0.02f), Iso.Floor(t.X + 0.54f, t.Y + 0.22f, 0.032f), Palette.Copper);
            b.Box(Iso.Floor(t.X + 0.22f, t.Y + 0.54f, 0.02f), Iso.Floor(t.X + 0.78f, t.Y + 0.46f, 0.032f), Palette.Copper);
        }

        /// <summary>
        /// Fades a block that would hide something behind it. `probes` are the screen boxes of what must stay readable, each with
        /// its depth: larger is nearer the viewer.
        /// </summary>
        public void TickFade(IsoCamera cam, List<(int Depth, Rect Box)> probes, float dt)
        {
            foreach (var b in _blocks)
            {
                var depth = b.Tile.X + b.Tile.Y;
                var box = cam.ScreenBox(Iso.Tile(b.Tile.X, b.Tile.Y), b.HalfWidth * 1.3f, b.Height);
                var fade = probes.Any(p => p.Depth < depth && p.Box.Overlaps(box));
                b.Alpha = Mathf.MoveTowards(b.Alpha, fade ? FadedAlpha : 1f, dt * 5f);
                b.Model.Paint(b.Alpha);
            }
        }
    }
}
