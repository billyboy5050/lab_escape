#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public static class AiCommon
    {
        /// <summary>The standard tie-break: nearest, then lowest current HP, then lowest unit ID.</summary>
        public static Unit? PickNearest(IReadOnlyList<Unit> cands, Func<Unit, int> dist)
        {
            return Pathing.MinBy(cands, u => new[] { dist(u), u.Hp, u.Id });
        }

        /// <summary>Nearest by range distance, for ranged and flying units.</summary>
        public static Unit? NearestRanged(Pos from, IReadOnlyList<Unit> cands) => PickNearest(cands, u => Grid.Cheb(from, u.Pos));

        /// <summary>Nearest by path length to an adjacent tile, for ground melee units. Falls back to range distance if none is reachable.</summary>
        public static Unit? NearestMelee(World w, Pos from, IReadOnlyList<Unit> cands)
        {
            var scored = cands.Select(u => new KeyValuePair<Unit, int>(u, Pathing.MeleeDistance(w, from, u.Pos))).ToList();
            var reachableOnes = scored.Where(x => x.Value < Pathing.Far).ToList();
            if (reachableOnes.Count > 0) return Pathing.MinBy(reachableOnes, x => new[] { x.Value, x.Key.Hp, x.Key.Id }).Key;
            return NearestRanged(from, cands);
        }

        /// <summary>Walks the unit to a destination from a reach map. Returns false if it died on the way.</summary>
        public static bool MoveTo(World w, Unit u, Reach reach, Pos dest)
        {
            if (dest == u.Pos) return !u.Dead;
            return w.MoveAlong(u, Pathing.PathTo(w, reach, dest), u.Flying ? MoveMode.Fly : MoveMode.Walk);
        }

        public static int StepsTo(World w, Reach reach, Pos p) => reach.Steps[w.B.Idx(p)];
    }
}
