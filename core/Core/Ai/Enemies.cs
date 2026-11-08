#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public sealed class GuardShot
    {
        public Unit Target = null!;
    }

    public sealed class ChargePlan
    {
        public Pos Dir;
        /// <summary>The player-side unit 2 to 5 tiles away that makes the lane worth charging.</summary>
        public Unit Trigger = null!;
        /// <summary>Distance to the first body or object in the lane; the Warden stops just before it.</summary>
        public int StopAt;
    }

    public static class EnemyAi
    {
        /// <summary>
        /// One enemy's action for the round: move first, then act. In the round a wave arrives, its units use only the movement
        /// part of their plan.
        /// </summary>
        public static void EnemyTurn(World w, Unit e)
        {
            var def = w.C.Units[e.Def];
            var arrival = e.ArrivalRound == w.S.Round;
            if (def.Charge != null) WardenTurn(w, e, arrival);
            else if (def.Sweep != null) FlamerTurn(w, e, arrival);
            else if (def.Heal != null) MedicTurn(w, e, arrival);
            else if (def.Attack != null) GuardTurn(w, e, arrival);
            else BruteTurn(w, e, arrival);
        }

        private static void Decide(World w, Unit u, string summary, List<AiOption>? options = null)
        {
            w.Emit(new AiDecision { Id = u.Id, Def = u.Def, Summary = summary, Options = options });
        }

        // ------------------------------------------------------------------------------------------ Guard

        /// <summary>
        /// A tile gives a shot at a player-side unit in range and sight when the first thing on the line is on the player's side
        /// (that unit, a minion, a shield segment or an egg). An enemy ally, the shutter or the wall gun first on the line spoils
        /// it. Returns the nearest such unit (ties: lowest HP, lowest ID).
        /// </summary>
        public static GuardShot? GuardShotFrom(World w, Unit g, Pos tile)
        {
            var atk = w.C.Units[g.Def].Attack!;
            var cands = new List<Unit>();
            foreach (var u in w.PlayerSide())
            {
                if (Grid.Cheb(tile, u.Pos) > atk.Range || !w.B.HasLos(tile, u.Pos)) continue;
                var tr = Projectiles.TraceProjectile(w, Projectiles.ShotPath(tile, u.Pos), ignoreUnit: g.Id);
                var first = tr.HitUnit != null ? tr.HitUnit.Team : tr.HitObject?.Team;
                if (first != Team.Player) continue;
                cands.Add(u);
            }
            var target = Pathing.MinBy(cands, u => new[] { Grid.Cheb(tile, u.Pos), u.Hp, u.Id });
            return target != null ? new GuardShot { Target = target } : null;
        }

        private sealed class FiringTile
        {
            public Pos D;
            public GuardShot Shot = null!;
        }

        public static void GuardTurn(World w, Unit g, bool arrival)
        {
            var dest = g.Pos;
            if (GuardShotFrom(w, g, g.Pos) != null)
            {
                Decide(w, g, $"Holds {Tiles.Name(g.Pos)}: it has a shot");
            }
            else
            {
                var reach = Pathing.Reachable(w, g, w.MovementAllowance(g));
                var firing = new List<FiringTile>();
                foreach (var d in reach.Dests)
                {
                    if (d == g.Pos) continue;
                    var shot = GuardShotFrom(w, g, d);
                    if (shot != null) firing.Add(new FiringTile { D = d, Shot = shot });
                }
                var best = Pathing.MinBy(firing, o => new[] { AiCommon.StepsTo(w, reach, o.D), Grid.Cheb(o.D, o.Shot.Target.Pos), o.D.Y, o.D.X });
                if (best != null)
                {
                    dest = best.D;
                    Decide(w, g, $"Moves to firing tile {Tiles.Name(dest)}");
                }
                else
                {
                    var t = AiCommon.NearestRanged(g.Pos, w.PlayerSide());
                    if (t != null) dest = Pathing.ApproachDest(w, reach, t.Pos);
                    Decide(w, g, $"No firing tile in reach: closes in to {Tiles.Name(dest)}");
                }
                if (!AiCommon.MoveTo(w, g, reach, dest)) return;
            }
            if (arrival) return;
            var finalShot = GuardShotFrom(w, g, g.Pos);
            if (finalShot == null) return;
            var atk = w.C.Units[g.Def].Attack!;
            Projectiles.FireProjectile(w, g.Pos, Projectiles.ShotPath(g.Pos, finalShot.Target.Pos), new ShotPayload { Source = w.UnitSource(g, "shot"), Damage = atk.Damage, DamageType = atk.DamageType, Reflectable = true });
        }

        // ------------------------------------------------------------------------------------------ Medic

        private static List<Unit> DamagedAllies(World w, Unit m) => w.Enemies().Where(u => u.Id != m.Id && u.Hp < u.MaxHp).ToList();

        public static void MedicTurn(World w, Unit m, bool arrival)
        {
            var def = w.C.Units[m.Def];
            var heal = def.Heal!;
            var safe = def.Ai!["safeDistance"];
            var follow = def.Ai!["followDistance"];
            var reach = Pathing.Reachable(w, m, w.MovementAllowance(m));
            int AwayFromPlayerSide(Pos t)
            {
                var d = Pathing.Far;
                foreach (var u in w.PlayerSide()) d = Math.Min(d, Grid.Cheb(t, u.Pos));
                return d;
            }
            // Prefer tiles at least `safe` from the nearest player-side unit (fewest steps); otherwise the farthest.
            Pos Pick(List<Pos> cands) => Pathing.MinBy(cands, t =>
            {
                var d = AwayFromPlayerSide(t);
                return d >= safe
                    ? new[] { 0, AiCommon.StepsTo(w, reach, t), 0, t.Y, t.X }
                    : new[] { 1, -d, AiCommon.StepsTo(w, reach, t), t.Y, t.X };
            });
            var ally = Pathing.MinBy(DamagedAllies(w, m), u => new[] { u.Hp, u.Id });
            var dest = m.Pos;
            if (ally != null)
            {
                var cands = reach.Dests.Where(t => Grid.Cheb(t, ally.Pos) <= heal.Range && w.B.HasLos(t, ally.Pos)).ToList();
                dest = cands.Count > 0 ? Pick(cands) : Pathing.ApproachDest(w, reach, ally.Pos);
                Decide(w, m, $"Goes to heal {ally.Def} {ally.Id} from {Tiles.Name(dest)}");
            }
            else
            {
                var guards = w.Enemies().Where(u => u.Def == "guard").ToList();
                var lead = AiCommon.NearestRanged(m.Pos, guards.Count > 0 ? guards : w.Enemies().Where(u => u.Id != m.Id).ToList());
                if (lead != null)
                {
                    var cands = reach.Dests.Where(t => Grid.Cheb(t, lead.Pos) <= follow).ToList();
                    dest = cands.Count > 0 ? Pick(cands) : Pathing.ApproachDest(w, reach, lead.Pos);
                    Decide(w, m, $"Nobody hurt: follows {lead.Def} {lead.Id} to {Tiles.Name(dest)}");
                }
            }
            if (!AiCommon.MoveTo(w, m, reach, dest) || arrival) return;
            var target = Pathing.MinBy(
                DamagedAllies(w, m).Where(u => Grid.Cheb(u.Pos, m.Pos) <= heal.Range && w.B.HasLos(m.Pos, u.Pos)).ToList(),
                u => new[] { u.Hp, u.Id });
            if (target != null) w.Heal(target, heal.Amount, w.UnitSource(m, "heal"));
        }

        // ------------------------------------------------------------------------------------------ Flamer

        /// <summary>The sweep line: up to `length` tiles in a straight line starting adjacent. Walls and pillars end it.</summary>
        public static List<Pos> SweepTiles(World w, Pos from, Pos dir, int length)
        {
            var output = new List<Pos>();
            for (var k = 1; k <= length; k++)
            {
                var p = Grid.Add(from, dir, k);
                if (w.B.BlocksMove(p)) break;
                output.Add(p);
            }
            return output;
        }

        /// <summary>Scores every reachable tile and sweep direction: +2 per player-side unit, +4 more for the player, -3 per ally.</summary>
        public static List<AiOption> FlamerOptions(World w, Unit f, IReadOnlyList<Pos> tiles, Func<Pos, int> steps)
        {
            var def = w.C.Units[f.Def];
            var ai = def.Ai!;
            var perSide = ai["scorePerPlayerSideHit"];
            var playerBonus = ai["scorePlayerBonus"];
            var perAlly = ai["scorePerAllyHit"];
            var units = w.ActiveUnits().Where(u => u.Id != f.Id).ToList();
            var output = new List<AiOption>();
            foreach (var t in tiles)
            {
                foreach (var d in Grid.Dirs4)
                {
                    var line = SweepTiles(w, t, d, def.Sweep!.Length);
                    var hits = units.Where(u => line.Contains(u.Pos)).ToList();
                    var score = 0;
                    foreach (var u in hits)
                    {
                        if (u.Team == Team.Player) score += perSide + (u.Id == 0 ? playerBonus : 0);
                        else score += perAlly;
                    }
                    output.Add(new AiOption { Tile = t, Dir = d, Score = score, Steps = steps(t), Hits = hits.Select(u => u.Id).ToList() });
                }
            }
            return output;
        }

        private static int DirOrder(Pos dir)
        {
            for (var i = 0; i < Grid.Dirs4.Count; i++)
            {
                if (Grid.Dirs4[i] == dir) return i;
            }
            return -1;
        }

        /// <summary>Highest positive score; ties: shortest move, then lowest tile, then direction order N, E, S, W.</summary>
        public static AiOption? BestFlamerOption(IEnumerable<AiOption> opts)
        {
            return Pathing.MinBy(opts.Where(o => o.Score > 0).ToList(), o => new[] { -o.Score, o.Steps, o.Tile.Y, o.Tile.X, DirOrder(o.Dir) });
        }

        public static void FlamerTurn(World w, Unit f, bool arrival)
        {
            var reach = Pathing.Reachable(w, f, w.MovementAllowance(f));
            var opts = FlamerOptions(w, f, reach.Dests, p => AiCommon.StepsTo(w, reach, p));
            var best = BestFlamerOption(opts);
            var dest = f.Pos;
            if (best != null)
            {
                dest = best.Tile;
                Decide(w, f, $"Best sweep scores {best.Score}: from {Tiles.Name(best.Tile)} facing {Grid.DirName(best.Dir)}", opts.Where(o => o.Score != 0).ToList());
            }
            else
            {
                var t = AiCommon.NearestMelee(w, f.Pos, w.PlayerSide());
                if (t != null) dest = Pathing.ApproachDest(w, reach, t.Pos);
                Decide(w, f, $"No positive sweep: moves toward {(t != null ? $"{t.Def} {t.Id}" : "nobody")}", new List<AiOption>());
            }
            if (!AiCommon.MoveTo(w, f, reach, dest) || arrival) return;
            var here = BestFlamerOption(FlamerOptions(w, f, new[] { f.Pos }, _ => 0));
            if (here == null) return;
            var sw = w.C.Units[f.Def].Sweep!;
            var tiles = SweepTiles(w, f.Pos, here.Dir, sw.Length);
            var src = w.UnitSource(f, "sweep");
            w.Emit(new AreaEffect { Kind = AreaKind.Sweep, Tiles = tiles, Source = src });
            w.AreaDamage(tiles, sw.Damage, sw.DamageType, src, exclude: f.Id);
        }

        // ------------------------------------------------------------------------------------------ Warden

        /// <summary>
        /// Charge lanes from a tile. A player-side unit must stand minDistance to maxMove+1 tiles away in a straight orthogonal
        /// line, with no wall, pillar, blocking terrain or enemy ally in the lane. The Warden stops against the first player-side
        /// unit or blocking object, and must move at least one tile.
        /// </summary>
        public static List<ChargePlan> ChargePlans(World w, Unit wd, Pos from, Func<Unit, bool>? only = null, int? maxMoveOverride = null)
        {
            var ch = w.C.Units[wd.Def].Charge!;
            var maxMove = maxMoveOverride ?? ChargeMove(w, wd);
            var plans = new List<ChargePlan>();
            if (maxMove < 1) return plans;
            foreach (var d in Grid.Dirs4)
            {
                var stopAt = 0;
                for (var k = 1; k <= maxMove + 1; k++)
                {
                    var p = Grid.Add(from, d, k);
                    if (w.B.BlocksMove(p)) break;
                    var u = w.UnitAt(p);
                    if (u != null && u.Id == wd.Id) continue;
                    if (u != null && u.Team == Team.Enemy) break;
                    if (stopAt == 0 && (u != null || w.BlockingObjectAt(p) != null)) stopAt = k;
                    if (stopAt == 1) break;
                    if (u != null && u.Team == Team.Player && k >= ch.MinDistance && (only == null || only(u)))
                    {
                        plans.Add(new ChargePlan { Dir = d, Trigger = u, StopAt = stopAt });
                        break;
                    }
                }
            }
            return plans;
        }

        /// <summary>Charge distance this round: Pinned stops it, Slowed shortens it.</summary>
        public static int ChargeMove(World w, Unit wd)
        {
            var ch = w.C.Units[wd.Def].Charge!;
            if (wd.Statuses.Pinned != null) return 0;
            return ch.MaxMove - (wd.Statuses.Slowed != null ? w.C.Rules.Statuses.Slowed.MovementPenalty : 0);
        }

        public static ChargePlan? BestCharge(IReadOnlyList<ChargePlan> plans, Pos from)
        {
            return Pathing.MinBy(plans, p => new[] { Grid.Cheb(from, p.Trigger.Pos), p.Trigger.Hp, p.Trigger.Id });
        }

        private static void PerformCharge(World w, Unit wd, Pos dir)
        {
            var ch = w.C.Units[wd.Def].Charge!;
            var from = wd.Pos;
            var path = new List<Pos>();
            var cur = wd.Pos;
            var maxMove = ChargeMove(w, wd);
            for (var k = 0; k < maxMove; k++)
            {
                var next = Grid.Add(cur, dir);
                if (w.B.BlocksMove(next) || w.UnitAt(next) != null || w.BlockingObjectAt(next) != null) break;
                path.Add(next);
                cur = next;
            }
            if (!w.MoveAlong(wd, path, MoveMode.Charge)) return;
            var front = Grid.Add(wd.Pos, dir);
            var hitUnit = w.UnitAt(front);
            var hitObj = hitUnit != null ? null : w.BlockingObjectAt(front);
            var src = w.UnitSource(wd, "charge");
            w.Emit(new Charged
            {
                Id = wd.Id,
                From = from,
                To = wd.Pos,
                Dir = dir,
                Hit = hitUnit != null ? EntityRef.Unit(hitUnit.Id) : hitObj != null ? EntityRef.Object(hitObj.Id) : (EntityRef?)null,
            });
            if (hitUnit != null && hitUnit.Team == Team.Player) w.DamageUnit(hitUnit, ch.Damage, ch.DamageType, src);
            else if (hitObj != null) w.DamageObject(hitObj, ch.Damage, ch.DamageType, src);
        }

        private static void Melee(World w, Unit attacker, Unit target)
        {
            var m = w.C.Units[attacker.Def].Melee!;
            w.DamageUnit(target, m.Damage, m.DamageType, w.UnitSource(attacker, "melee"));
        }

        public static void WardenTurn(World w, Unit wd, bool arrival)
        {
            var def = w.C.Units[wd.Def];
            if (!arrival && def.Purge != null && CooldownOf(wd, "purge") == 0 && wd.Statuses.Poison != null)
            {
                w.RemoveStatus(wd, StatusId.Poison, "purge");
                wd.Cooldowns["purge"] = def.Purge.Cooldown;
                wd.PurgeImmuneRound = w.S.Round;
                w.Emit(new Purged { Id = wd.Id });
            }
            var player = w.Player;
            if (!arrival)
            {
                var plan = BestCharge(ChargePlans(w, wd, wd.Pos), wd.Pos);
                if (plan != null)
                {
                    Decide(w, wd, $"Charges {Grid.DirName(plan.Dir)} at {plan.Trigger.Def} {plan.Trigger.Id}");
                    PerformCharge(w, wd, plan.Dir);
                    return;
                }
                if (player != null && Grid.Cheb(wd.Pos, player.Pos) == 1)
                {
                    Decide(w, wd, "Melees the intruder");
                    Melee(w, wd, player);
                    return;
                }
            }
            if (player == null) return;
            var reach = Pathing.Reachable(w, wd, w.MovementAllowance(wd));
            var field = Pathing.DistanceField(w, Pathing.AdjacentGoals(w, player.Pos));
            int At(Pos p) => field[w.B.Idx(p)];
            var closer = reach.Dests.Where(t => At(t) < At(wd.Pos)).ToList();
            if (closer.Count == 0)
            {
                if (arrival) return;
                var target = Pathing.MinBy(w.PlayerSide().Where(u => Grid.Cheb(u.Pos, wd.Pos) == 1).ToList(), u => new[] { 1, u.Hp, u.Id });
                Decide(w, wd, target != null ? $"Cannot get closer: melees {target.Def} {target.Id}" : "Cannot get closer and nothing adjacent");
                if (target != null) Melee(w, wd, target);
                return;
            }
            // Lining up a lane for next round: statuses that limit this round's charge will have worn off.
            var fullCharge = def.Charge!.MaxMove;
            var lane = closer.Where(t => ChargePlans(w, wd, t, u => u.Id == 0, fullCharge).Count > 0).ToList();
            var pool = lane.Count > 0 ? lane : closer;
            var dest = Pathing.MinBy(pool, t => new[] { At(t), AiCommon.StepsTo(w, reach, t), t.Y, t.X });
            Decide(w, wd, lane.Count > 0 ? $"Approaches to {Tiles.Name(dest)}, lining up a charge lane" : $"Approaches to {Tiles.Name(dest)}");
            AiCommon.MoveTo(w, wd, reach, dest);
        }

        private static int CooldownOf(Unit u, string key) => u.Cooldowns.TryGetValue(key, out var v) ? v : 0;

        /// <summary>Fallback for an enemy with only a melee attack (not used by the MVP roster).</summary>
        private static void BruteTurn(World w, Unit e, bool arrival)
        {
            var t = AiCommon.NearestMelee(w, e.Pos, w.PlayerSide());
            if (t == null) return;
            if (Grid.Cheb(e.Pos, t.Pos) > 1)
            {
                var reach = Pathing.Reachable(w, e, w.MovementAllowance(e));
                if (!AiCommon.MoveTo(w, e, reach, Pathing.ApproachDest(w, reach, t.Pos))) return;
            }
            if (arrival || w.C.Units[e.Def].Melee == null) return;
            var adj = Pathing.MinBy(w.PlayerSide().Where(u => Grid.Cheb(u.Pos, e.Pos) == 1).ToList(), u => new[] { u.Hp, u.Id });
            if (adj != null) Melee(w, e, adj);
        }
    }
}
