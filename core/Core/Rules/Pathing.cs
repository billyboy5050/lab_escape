#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public sealed class Reach
    {
        /// <summary>Steps to each tile index, -1 where unreachable.</summary>
        public int[] Steps = null!;
        public int[] Prev = null!;
        /// <summary>Tiles the unit could end on, including its own tile, sorted by steps then row-major.</summary>
        public List<Pos> Dests = null!;
    }

    public static class Pathing
    {
        public const int Far = 9999;

        private static int[] Filled(int n, int value)
        {
            var a = new int[n];
            for (var i = 0; i < n; i++) a[i] = value;
            return a;
        }

        private static bool[] BlockedByObjects(World w)
        {
            var b = w.B;
            var blocked = new bool[b.Width * b.Height];
            foreach (var o in w.S.Objects)
            {
                if (!o.Destroyed && (o.Kind == ObjectKind.Shield || o.Kind == ObjectKind.Egg)) blocked[b.Idx(o.Pos)] = true;
            }
            return blocked;
        }

        /// <summary>
        /// Breadth-first search over 4-direction steps (north, east, south, west, in that order). Ground units cannot pass
        /// through units; flying units can pass over units but not end on them. Nobody passes through walls, pillars, the
        /// shutter, the wall gun, shield segments or eggs.
        /// </summary>
        public static Reach Reachable(World w, Unit u, int maxSteps)
        {
            var b = w.B;
            var n = b.Width * b.Height;
            var steps = Filled(n, -1);
            var prev = Filled(n, -1);
            steps[b.Idx(u.Pos)] = 0;
            var queue = new List<Pos> { u.Pos };
            var occupied = new bool[n];
            foreach (var o in w.ActiveUnits())
            {
                if (o.Id != u.Id) occupied[b.Idx(o.Pos)] = true;
            }
            var blocked = BlockedByObjects(w);
            for (var qi = 0; qi < queue.Count; qi++)
            {
                var p = queue[qi];
                var sp = steps[b.Idx(p)];
                if (sp >= maxSteps) continue;
                foreach (var d in Grid.Dirs4)
                {
                    var q = Grid.Add(p, d);
                    if (b.BlocksMove(q)) continue;
                    var qi2 = b.Idx(q);
                    if (steps[qi2] != -1 || blocked[qi2]) continue;
                    if (!u.Flying && occupied[qi2]) continue;
                    steps[qi2] = sp + 1;
                    prev[qi2] = b.Idx(p);
                    queue.Add(q);
                }
            }
            var dests = Sequences.StableSorted(queue.Where(p => !occupied[b.Idx(p)]), (a, c) =>
            {
                var bySteps = steps[b.Idx(a)] - steps[b.Idx(c)];
                if (bySteps != 0) return bySteps;
                return a.Y != c.Y ? a.Y - c.Y : a.X - c.X;
            });
            return new Reach { Steps = steps, Prev = prev, Dests = dests };
        }

        /// <summary>The path from the unit's tile to dest, excluding the start tile.</summary>
        public static List<Pos> PathTo(World w, Reach reach, Pos dest)
        {
            var b = w.B;
            var output = new List<Pos>();
            var i = b.Idx(dest);
            if (reach.Steps[i] == -1) throw new InvalidOperationException("pathTo: destination not reachable");
            while (reach.Steps[i] > 0)
            {
                output.Add(b.PosOf(i));
                i = reach.Prev[i];
            }
            output.Reverse();
            return output;
        }

        /// <summary>
        /// Ground path distance from every tile to the nearest goal tile, ignoring units (they move) but respecting terrain,
        /// shield segments and eggs. Unreachable tiles hold Far.
        /// </summary>
        public static int[] DistanceField(World w, IReadOnlyList<Pos> goals)
        {
            var b = w.B;
            var n = b.Width * b.Height;
            var dist = Filled(n, Far);
            var blocked = BlockedByObjects(w);
            var queue = new List<Pos>();
            foreach (var g in goals)
            {
                var gi = b.Idx(g);
                if (dist[gi] == Far)
                {
                    dist[gi] = 0;
                    queue.Add(g);
                }
            }
            for (var qi = 0; qi < queue.Count; qi++)
            {
                var p = queue[qi];
                var dp = dist[b.Idx(p)];
                foreach (var d in Grid.Dirs4)
                {
                    var q = Grid.Add(p, d);
                    if (b.BlocksMove(q)) continue;
                    var i = b.Idx(q);
                    if (blocked[i] || dist[i] != Far) continue;
                    dist[i] = dp + 1;
                    queue.Add(q);
                }
            }
            return dist;
        }

        /// <summary>Tiles from which a melee unit can hit the target: its 8 neighbours that a ground unit could stand on.</summary>
        public static List<Pos> AdjacentGoals(World w, Pos target)
        {
            return w.B.Neighbors8(target).Where(p => !w.B.BlocksMove(p) && w.BlockingObjectAt(p) == null).ToList();
        }

        /// <summary>Path length (ignoring units) for a ground unit at `from` to get adjacent to `target`.</summary>
        public static int MeleeDistance(World w, Pos from, Pos target)
        {
            if (Grid.Cheb(from, target) <= 1) return 0;
            return DistanceField(w, AdjacentGoals(w, target))[w.B.Idx(from)];
        }

        /// <summary>
        /// The reachable destination that gets closest to a target: by path distance to an adjacent tile (ignoring units), then
        /// range distance, then fewest steps, then lowest row, then lowest column.
        /// </summary>
        public static Pos ApproachDest(World w, Reach reach, Pos target)
        {
            var field = DistanceField(w, AdjacentGoals(w, target));
            return MinBy(reach.Dests, d =>
            {
                var i = w.B.Idx(d);
                return new[] { field[i], Grid.Cheb(d, target), reach.Steps[i], d.Y, d.X };
            });
        }

        public static bool LexLess(IReadOnlyList<int> a, IReadOnlyList<int> b)
        {
            for (var i = 0; i < a.Count; i++)
            {
                if (a[i] < b[i]) return true;
                if (a[i] > b[i]) return false;
            }
            return false;
        }

        /// <summary>
        /// Picks the item with the smallest key (lexicographic). Ties keep the earliest item. Returns the type's default for an
        /// empty list, so callers holding a struct check the list is not empty first.
        /// </summary>
        public static T MinBy<T>(IReadOnlyList<T> items, Func<T, int[]> key)
        {
            var found = false;
            var best = default(T)!;
            var bestKey = Array.Empty<int>();
            foreach (var item in items)
            {
                var k = key(item);
                if (!found || LexLess(k, bestKey))
                {
                    found = true;
                    best = item;
                    bestKey = k;
                }
            }
            return best;
        }
    }
}
