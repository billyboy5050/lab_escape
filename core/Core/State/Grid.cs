#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public static class Grid
    {
        public static readonly Pos N = new Pos(0, -1);
        public static readonly Pos E = new Pos(1, 0);
        public static readonly Pos S = new Pos(0, 1);
        public static readonly Pos W = new Pos(-1, 0);

        /// <summary>Fixed order for every 4-direction scan: north, east, south, west.</summary>
        public static readonly IReadOnlyList<Pos> Dirs4 = new[] { N, E, S, W };

        /// <summary>The 8 neighbours in row-major order (lowest row, then lowest column).</summary>
        public static readonly IReadOnlyList<Pos> Dirs8 = new[]
        {
            new Pos(-1, -1), new Pos(0, -1), new Pos(1, -1),
            new Pos(-1, 0), new Pos(1, 0),
            new Pos(-1, 1), new Pos(0, 1), new Pos(1, 1),
        };

        public static Pos DirByName(string name)
        {
            switch (name)
            {
                case "N": return N;
                case "E": return E;
                case "S": return S;
                case "W": return W;
                default: throw new ArgumentException($"Unknown direction \"{name}\"");
            }
        }

        public static Pos Add(Pos a, Pos d, int k = 1) => new Pos(a.X + d.X * k, a.Y + d.Y * k);

        /// <summary>Range distance: the larger of the column and row difference.</summary>
        public static int Cheb(Pos a, Pos b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

        public static int Manhattan(Pos a, Pos b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        /// <summary>Row-major order: lowest row, then lowest column.</summary>
        public static int ComparePos(Pos a, Pos b) => a.Y != b.Y ? a.Y - b.Y : a.X - b.X;

        public static string DirName(Pos d) => d.Y < 0 ? "N" : d.X > 0 ? "E" : d.Y > 0 ? "S" : "W";

        /// <summary>Standard all-octant integer Bresenham from a to b, both endpoints included.</summary>
        public static List<Pos> Bresenham(Pos a, Pos b)
        {
            var pts = new List<Pos>();
            var x0 = a.X;
            var y0 = a.Y;
            var dx = Math.Abs(b.X - x0);
            var sx = x0 < b.X ? 1 : -1;
            var dy = -Math.Abs(b.Y - y0);
            var sy = y0 < b.Y ? 1 : -1;
            var err = dx + dy;
            for (; ; )
            {
                pts.Add(new Pos(x0, y0));
                if (x0 == b.X && y0 == b.Y) break;
                var e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
            return pts;
        }

        /// <summary>
        /// The tile list of the line between a and b, ordered from a to b. The line is always traced from the tile with the
        /// lower row, then the lower column, and the same list is used in both directions, so sight and projectile paths are
        /// symmetric.
        /// </summary>
        public static List<Pos> LineTiles(Pos a, Pos b)
        {
            var aFirst = a.Y < b.Y || (a.Y == b.Y && a.X <= b.X);
            return aFirst ? Bresenham(a, b) : Sequences.Reversed(Bresenham(b, a));
        }
    }

    public sealed class Board
    {
        public int Width { get; }
        public int Height { get; }
        private readonly TerrainDef[] _terrain;
        private readonly string[] _terrainIds;

        public Board(MapFile map)
        {
            Width = map.Width;
            Height = map.Height;
            _terrain = new TerrainDef[map.Width * map.Height];
            _terrainIds = new string[map.Width * map.Height];
            var i = 0;
            for (var y = 0; y < map.Height; y++)
            {
                for (var x = 0; x < map.Width; x++)
                {
                    var id = map.Legend[map.Rows[y][x].ToString()];
                    _terrainIds[i] = id;
                    _terrain[i] = map.Terrain[id];
                    i++;
                }
            }
        }

        public bool InBounds(Pos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;
        public int Idx(Pos p) => p.Y * Width + p.X;
        public Pos PosOf(int i) => new Pos(i % Width, i / Width);
        public string TerrainId(Pos p) => _terrainIds[Idx(p)];

        /// <summary>Out-of-bounds counts as blocking everything.</summary>
        public bool BlocksMove(Pos p) => !InBounds(p) || _terrain[Idx(p)].BlocksMove;
        public bool BlocksSight(Pos p) => !InBounds(p) || _terrain[Idx(p)].BlocksSight;
        public bool BlocksProjectile(Pos p) => !InBounds(p) || _terrain[Idx(p)].BlocksProjectile;

        /// <summary>Line of sight: no pillar or wall strictly between the two tiles. Symmetric by construction.</summary>
        public bool HasLos(Pos a, Pos b)
        {
            var pts = Grid.LineTiles(a, b);
            for (var i = 1; i < pts.Count - 1; i++)
            {
                if (BlocksSight(pts[i])) return false;
            }
            return true;
        }

        /// <summary>Tiles within Chebyshev radius of the centre, in bounds, row-major. Includes the centre.</summary>
        public List<Pos> Square(Pos center, int radius)
        {
            var output = new List<Pos>();
            for (var y = center.Y - radius; y <= center.Y + radius; y++)
            {
                for (var x = center.X - radius; x <= center.X + radius; x++)
                {
                    var p = new Pos(x, y);
                    if (InBounds(p)) output.Add(p);
                }
            }
            return output;
        }

        public List<Pos> Neighbors8(Pos p) => Grid.Dirs8.Select(d => Grid.Add(p, d)).Where(InBounds).ToList();

        public List<Pos> AllTiles()
        {
            var output = new List<Pos>(Width * Height);
            for (var y = 0; y < Height; y++)
            {
                for (var x = 0; x < Width; x++) output.Add(new Pos(x, y));
            }
            return output;
        }
    }
}
