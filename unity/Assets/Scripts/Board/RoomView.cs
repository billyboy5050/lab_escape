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

            var mesh = new Mesh { name = "room" };
            b.Apply(mesh);
            var go = new GameObject("Room", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = Mats.Solid;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;

            // The near sides leave room for the coordinates.
            BoundsMin = new Vector3(-edge, -0.3f, -(h2 + CoordOut + 0.2f));
            BoundsMax = new Vector3(w + CoordOut + 0.2f, BackWall, edge);
        }

        private static bool IsBlock(string id) => id == "wall" || id == "pillar" || id == "shutter" || id == "gun";

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
