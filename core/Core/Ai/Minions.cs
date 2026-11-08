#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public static class MinionAi
    {
        /// <summary>
        /// One minion's action for the round: move first, then act. Minions never pick a friendly unit as a target; friendly fire
        /// comes from blast areas, chains and projectile lines.
        /// </summary>
        public static void MinionTurn(World w, Unit m)
        {
            if (m.Docked) return;
            var def = w.C.Units[m.Def];
            if (def.Ammo.HasValue) TurretTurn(w, m);
            else if (def.Zap != null) DroneTurn(w, m);
            else if (def.Blast != null) BursterTurn(w, m);
            else if (def.Attack != null) SpitterTurn(w, m);
            else if (def.Melee != null) HatchlingTurn(w, m);
        }

        /// <summary>Fires at the nearest enemy within range and sight, using 1 ammo. At 0 ammo it stays as a blocker.</summary>
        public static void TurretTurn(World w, Unit m)
        {
            var atk = w.C.Units[m.Def].Attack!;
            if ((m.Ammo ?? 0) <= 0) return;
            var target = AiCommon.NearestRanged(m.Pos, w.Enemies().Where(e => Grid.Cheb(e.Pos, m.Pos) <= atk.Range && w.B.HasLos(m.Pos, e.Pos)).ToList());
            if (target == null) return;
            m.Ammo = (m.Ammo ?? 0) - 1;
            var src = w.UnitSource(m, "turret_shot");
            var slow = w.HasUp(Up.T3) ? w.UpDef(Up.T3).Status : null;
            Action<Unit>? onHit = null;
            if (slow != null)
            {
                onHit = u =>
                {
                    if (u.Team == Team.Enemy) w.ApplyStatus(u, slow.Id, slow.Duration, src.WithVia(Up.T3));
                };
            }
            Projectiles.FireProjectile(w, m.Pos, Projectiles.ShotPath(m.Pos, target.Pos), new ShotPayload
            {
                Source = src,
                Damage = atk.Damage,
                DamageType = atk.DamageType,
                ArmorIgnore = w.Piercing(atk.DamageType),
                Reflectable = true,
                OnHitUnit = onHit,
            });
        }

        /// <summary>
        /// Drone cycle. Deployed: fly toward the nearest enemy until it is within zap range with sight, then zap it. Returning (0
        /// charges): fly toward the player without attacking, and dock when adjacent or after the set number of returning phases.
        /// </summary>
        public static void DroneTurn(World w, Unit m)
        {
            var def = w.C.Units[m.Def];
            var zap = def.Zap!;
            var player = w.Player;
            if (m.DroneState == DroneState.Returning)
            {
                if (player != null && Grid.Cheb(m.Pos, player.Pos) <= 1)
                {
                    Dock(w, m);
                    return;
                }
                if (player != null)
                {
                    var homeReach = Pathing.Reachable(w, m, w.MovementAllowance(m, def.ReturnMove ?? def.Move));
                    var home = Pathing.MinBy(homeReach.Dests, t => new[] { Grid.Cheb(t, player.Pos), AiCommon.StepsTo(w, homeReach, t), t.Y, t.X });
                    if (!AiCommon.MoveTo(w, m, homeReach, home)) return;
                }
                m.ReturningPhases = (m.ReturningPhases ?? 0) + 1;
                if ((player != null && Grid.Cheb(m.Pos, player.Pos) <= 1) || m.ReturningPhases.Value >= def.ReturnPhases!.Value) Dock(w, m);
                return;
            }
            bool InRange(Pos t, Unit e) => Grid.Cheb(t, e.Pos) <= zap.Range && w.B.HasLos(t, e.Pos);
            var target = AiCommon.NearestRanged(m.Pos, w.Enemies());
            if (target != null && !InRange(m.Pos, target))
            {
                var reach = Pathing.Reachable(w, m, w.MovementAllowance(m));
                var good = reach.Dests.Where(t => InRange(t, target)).ToList();
                var dest = good.Count > 0
                    ? Pathing.MinBy(good, t => new[] { AiCommon.StepsTo(w, reach, t), Grid.Cheb(t, target.Pos), t.Y, t.X })
                    : Pathing.MinBy(reach.Dests, t => new[] { Grid.Cheb(t, target.Pos), AiCommon.StepsTo(w, reach, t), t.Y, t.X });
                if (!AiCommon.MoveTo(w, m, reach, dest)) return;
            }
            var zt = AiCommon.NearestRanged(m.Pos, w.Enemies().Where(e => InRange(m.Pos, e)).ToList());
            if (zt == null) return;
            DroneZap(w, m, zt);
            m.Charges = (m.Charges ?? 1) - 1;
            if (m.Charges.Value <= 0)
            {
                m.DroneState = DroneState.Returning;
                m.ReturningPhases = 0;
            }
        }

        /// <summary>
        /// Zap: electric damage to the target, chaining to every other unit adjacent to it (never the drone itself). Overcharged
        /// Drone adds hops to units adjacent to chained targets; Plague Drone poisons everything hit.
        /// </summary>
        public static void DroneZap(World w, Unit m, Unit target)
        {
            var zap = w.C.Units[m.Def].Zap!;
            var src = w.UnitSource(m, "zap");
            var hops = w.HasUp(Up.T6) ? w.UpDef(Up.T6).ExtraChainHops ?? 0 : 0;
            var hit = new HashSet<int> { target.Id };
            var chained = new List<Unit>();
            var frontier = new List<Unit> { target };
            w.Emit(new ChainArc { From = m.Pos, To = target.Pos, Source = src });
            for (var h = 0; h <= hops; h++)
            {
                var current = frontier;
                var next = Sequences.StableSorted(
                    w.ActiveUnits().Where(u => u.Id != m.Id && !hit.Contains(u.Id) && current.Any(f => Grid.Cheb(f.Pos, u.Pos) == 1)),
                    (a, b) => a.Id - b.Id);
                foreach (var u in next)
                {
                    hit.Add(u.Id);
                    chained.Add(u);
                    var via = current.First(f => Grid.Cheb(f.Pos, u.Pos) == 1);
                    w.Emit(new ChainArc { From = via.Pos, To = u.Pos, Source = src });
                }
                frontier = next;
            }
            w.DamageUnit(target, zap.Damage, zap.DamageType, src);
            foreach (var u in chained) w.DamageUnit(u, zap.ChainDamage, zap.DamageType, src);
            if (w.HasUp(Up.H2))
            {
                var st = w.UpDef(Up.H2).Status!;
                var psrc = src.WithVia(Up.H2);
                w.ApplyStatus(target, st.Id, st.Duration, psrc);
                foreach (var u in chained) w.ApplyStatus(u, st.Id, st.Duration, psrc);
            }
        }

        private static void Dock(World w, Unit m)
        {
            m.DroneState = DroneState.Docked;
            m.ReturningPhases = 0;
            w.Emit(new DroneDocked { Id = m.Id, Pos = m.Pos });
        }

        /// <summary>Basic hatchling: moves toward the nearest enemy and bites one adjacent enemy.</summary>
        public static void HatchlingTurn(World w, Unit m)
        {
            var target = AiCommon.NearestMelee(w, m.Pos, w.Enemies());
            if (target == null) return;
            if (Grid.Cheb(m.Pos, target.Pos) > 1)
            {
                var reach = Pathing.Reachable(w, m, w.MovementAllowance(m));
                if (!AiCommon.MoveTo(w, m, reach, Pathing.ApproachDest(w, reach, target.Pos))) return;
            }
            var bite = Pathing.MinBy(w.Enemies().Where(e => Grid.Cheb(e.Pos, m.Pos) == 1).ToList(), e => new[] { e.Hp, e.Id });
            var melee = w.C.Units[m.Def].Melee!;
            if (bite != null) w.DamageUnit(bite, melee.Damage, melee.DamageType, w.UnitSource(m, "bite"));
        }

        private sealed class SpitTile
        {
            public Pos T;
            public Unit S = null!;
        }

        /// <summary>Spitter: moves until an enemy is within range and sight, then spits acid at the nearest one.</summary>
        public static void SpitterTurn(World w, Unit m)
        {
            var atk = w.C.Units[m.Def].Attack!;
            Unit? ShotFrom(Pos t) => AiCommon.NearestRanged(t, w.Enemies().Where(e => Grid.Cheb(e.Pos, t) <= atk.Range && w.B.HasLos(t, e.Pos)).ToList());
            if (ShotFrom(m.Pos) == null)
            {
                var reach = Pathing.Reachable(w, m, w.MovementAllowance(m));
                var good = new List<SpitTile>();
                foreach (var t in reach.Dests)
                {
                    var s = ShotFrom(t);
                    if (s != null) good.Add(new SpitTile { T = t, S = s });
                }
                var dest = m.Pos;
                if (good.Count > 0)
                {
                    dest = Pathing.MinBy(good, x => new[] { AiCommon.StepsTo(w, reach, x.T), Grid.Cheb(x.T, x.S.Pos), x.T.Y, x.T.X }).T;
                }
                else
                {
                    var t = AiCommon.NearestRanged(m.Pos, w.Enemies());
                    if (t != null) dest = Pathing.ApproachDest(w, reach, t.Pos);
                }
                if (!AiCommon.MoveTo(w, m, reach, dest)) return;
            }
            var shot = ShotFrom(m.Pos);
            if (shot != null) Projectiles.FireProjectile(w, m.Pos, Projectiles.ShotPath(m.Pos, shot.Pos), new ShotPayload { Source = w.UnitSource(m, "spit"), Damage = atk.Damage, DamageType = atk.DamageType, Reflectable = true });
        }

        /// <summary>Burster: moves toward the nearest enemy, and when adjacent to one, detonates and dies.</summary>
        public static void BursterTurn(World w, Unit m)
        {
            bool AdjacentEnemy() => w.Enemies().Any(e => Grid.Cheb(e.Pos, m.Pos) == 1);
            if (!AdjacentEnemy())
            {
                var target = AiCommon.NearestMelee(w, m.Pos, w.Enemies());
                if (target == null) return;
                var reach = Pathing.Reachable(w, m, w.MovementAllowance(m));
                if (!AiCommon.MoveTo(w, m, reach, Pathing.ApproachDest(w, reach, target.Pos))) return;
            }
            if (AdjacentEnemy()) Detonate(w, m);
        }

        private static void Detonate(World w, Unit m)
        {
            var blast = w.C.Units[m.Def].Blast!;
            var src = w.UnitSource(m, "burst");
            m.Hp = 0;
            m.Dead = true;
            m.KilledBy = src;
            var tiles = w.B.Square(m.Pos, blast.Radius);
            w.Emit(new AreaEffect { Kind = AreaKind.Blast, Tiles = tiles, Source = src });
            w.AreaDamage(tiles, blast.Damage, blast.DamageType, src, exclude: m.Id);
        }
    }
}
