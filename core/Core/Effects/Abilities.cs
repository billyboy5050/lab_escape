#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public sealed class TargetOption
    {
        public Pos Target;
        public Pos? Target2;
        public string? Mode;

        public TargetOption(Pos target, Pos? target2 = null, string? mode = null)
        {
            Target = target;
            Target2 = target2;
            Mode = mode;
        }
    }

    public sealed class AbilityImpl
    {
        /// <summary>Every valid target option for the player right now. Empty means the ability cannot be committed.</summary>
        public Func<World, Unit, List<TargetOption>> Options = null!;
        public Action<World, Unit, TargetOption> Resolve = null!;
        /// <summary>Cap or availability check that disables the ability regardless of targets.</summary>
        public Func<World, string?>? Blocked;
    }

    public sealed class EggPlanResult
    {
        public string Host = "";
        public string HatchInto = "";
        public int Timer;
        public int Count;
        public int? CorpseId;
    }

    public static class Abilities
    {
        public const string ModeSelf = "self";
        public const string ModeUnit = "unit";

        /// <summary>Tiles within a Chebyshev range of the player, row-major.</summary>
        private static List<Pos> TilesWithin(World w, Pos center, int range) => w.B.Square(center, range);

        /// <summary>
        /// Projectile abilities can target any unit in range and sight. The shot itself hits the first unit or blocking object
        /// on the line, friendly or not. Targets whose line ends in a shutter or the wall gun are not offered.
        /// </summary>
        private static List<TargetOption> ProjectileOptions(World w, Unit p, int range, IEnumerable<Pos>? extraTargets = null)
        {
            var output = new List<TargetOption>();
            foreach (var u in w.ActiveUnits())
            {
                if (u.Id == p.Id || Grid.Cheb(p.Pos, u.Pos) > range || !w.B.HasLos(p.Pos, u.Pos)) continue;
                var tr = Projectiles.TraceProjectile(w, Projectiles.ShotPath(p.Pos, u.Pos));
                if (tr.HitUnit != null || tr.HitObject != null) output.Add(new TargetOption(u.Pos));
            }
            if (extraTargets != null)
            {
                foreach (var t in extraTargets)
                {
                    if (output.Any(o => o.Target == t)) continue;
                    output.Add(new TargetOption(t));
                }
            }
            return Sequences.StableSorted(output, (a, b) => a.Target.Y != b.Target.Y ? a.Target.Y - b.Target.Y : a.Target.X - b.Target.X);
        }

        private static void Shoot(World w, Unit p, Pos target, ShotPayload payload)
        {
            Projectiles.FireProjectile(w, p.Pos, Projectiles.ShotPath(p.Pos, target), payload);
        }

        private static readonly AbilityImpl Sidearm = new AbilityImpl
        {
            Options = (w, p) =>
            {
                var range = w.C.Abilities["sidearm"].Targeting.Range!.Value;
                var mines = new List<Pos>();
                if (w.HasUp(Up.T2))
                {
                    foreach (var m in w.S.Objects)
                    {
                        if (m.Kind != ObjectKind.Mine || m.Destroyed || Grid.Cheb(p.Pos, m.Pos) > range || !w.B.HasLos(p.Pos, m.Pos)) continue;
                        var tr = Projectiles.TraceProjectile(w, Projectiles.ShotPath(p.Pos, m.Pos), m.Pos);
                        if (tr.Mine != null || tr.HitUnit != null || tr.HitObject != null) mines.Add(m.Pos);
                    }
                }
                return ProjectileOptions(w, p, range, mines);
            },
            Resolve = (w, p, opt) =>
            {
                var def = w.C.Abilities["sidearm"];
                var mineTarget = w.HasUp(Up.T2) && w.MineAt(opt.Target) != null ? opt.Target : (Pos?)null;
                Shoot(w, p, opt.Target, new ShotPayload
                {
                    Source = w.UnitSource(p, "sidearm"),
                    Damage = def.Damage,
                    DamageType = def.DamageType,
                    ArmorIgnore = w.Piercing(def.DamageType),
                    MineTarget = mineTarget,
                    Reflectable = true,
                });
            },
        };

        private static readonly AbilityImpl AcidSpit = new AbilityImpl
        {
            Options = (w, p) => ProjectileOptions(w, p, w.C.Abilities["acid_spit"].Targeting.Range!.Value),
            Resolve = (w, p, opt) =>
            {
                var def = w.C.Abilities["acid_spit"];
                Shoot(w, p, opt.Target, new ShotPayload { Source = w.UnitSource(p, "acid_spit"), Damage = def.Damage, DamageType = def.DamageType, Corrode = def.Corrode, Reflectable = true });
            },
        };

        private static readonly AbilityImpl Parasite = new AbilityImpl
        {
            Options = (w, p) => ProjectileOptions(w, p, w.C.Abilities["parasite"].Targeting.Range!.Value),
            Resolve = (w, p, opt) =>
            {
                var def = w.C.Abilities["parasite"];
                Shoot(w, p, opt.Target, new ShotPayload { Source = w.UnitSource(p, "parasite"), Status = def.Status, Reflectable = true });
            },
        };

        private sealed class GrappleHit
        {
            public Unit Unit = null!;
            public List<Pos> Drag = null!;
        }

        /// <summary>The grapple's pull-unit target: the first thing on the line must be a pullable unit with room to move.</summary>
        private static GrappleHit? GrappleUnitHit(World w, Unit p, Pos target)
        {
            var path = Projectiles.ShotPath(p.Pos, target);
            var tr = Projectiles.TraceProjectile(w, path);
            if (tr.HitUnit == null || tr.HitUnit.Elite) return null;
            var k = tr.Path.Count - 1;
            if (k < 1) return null;
            return new GrappleHit { Unit = tr.HitUnit, Drag = Sequences.Reversed(path.Take(k).ToList()) };
        }

        /// <summary>Pull self: the tiles the player crosses, stopping on the last free tile before a blocker.</summary>
        public static List<Pos> GrappleSelfPath(World w, Unit p, Pos target)
        {
            var walk = new List<Pos>();
            foreach (var t in Projectiles.ShotPath(p.Pos, target))
            {
                if (!w.IsFree(t, p.Id)) break;
                walk.Add(t);
            }
            return walk;
        }

        private static readonly AbilityImpl Grapple = new AbilityImpl
        {
            Options = (w, p) =>
            {
                var range = w.C.Abilities["grapple_hook"].Targeting.Range!.Value;
                var output = new List<TargetOption>();
                foreach (var t in TilesWithin(w, p.Pos, range))
                {
                    if (t == p.Pos || !w.B.HasLos(p.Pos, t)) continue;
                    if (GrappleSelfPath(w, p, t).Count > 0) output.Add(new TargetOption(t, mode: ModeSelf));
                    var u = w.UnitAt(t);
                    if (u != null && u.Id != p.Id && GrappleUnitHit(w, p, t) != null) output.Add(new TargetOption(t, mode: ModeUnit));
                }
                return output;
            },
            Resolve = (w, p, opt) =>
            {
                var src = w.UnitSource(p, "grapple_hook");
                if (opt.Mode == ModeSelf)
                {
                    var walk = GrappleSelfPath(w, p, opt.Target);
                    w.Emit(new ProjectileFired { Source = src, From = p.Pos, Path = Projectiles.ShotPath(p.Pos, opt.Target), Hit = null, HitPos = null });
                    w.MoveAlong(p, walk, MoveMode.Grapple);
                    return;
                }
                var hit = GrappleUnitHit(w, p, opt.Target);
                var tr = Projectiles.TraceProjectile(w, Projectiles.ShotPath(p.Pos, opt.Target));
                w.Emit(new ProjectileFired { Source = src, From = p.Pos, Path = tr.Path, Hit = tr.HitUnit != null ? EntityRef.Unit(tr.HitUnit.Id) : (EntityRef?)null, HitPos = tr.HitPos });
                if (hit == null) return;
                var u = hit.Unit;
                w.MoveAlong(u, hit.Drag, MoveMode.Pull);
                if (!u.Dead && w.HasUp(Up.T4))
                {
                    var up = w.UpDef(Up.T4);
                    w.Immediate("u" + p.Id, Up.T4, () =>
                    {
                        var psrc = w.PlayerEffect(Up.T4);
                        w.DamageUnit(u, up.Damage ?? 0, up.DamageType ?? DamageType.Kinetic, psrc);
                        if (up.Status != null) w.ApplyStatus(u, up.Status.Id, up.Status.Duration, psrc);
                    });
                }
            },
        };

        private static string? MineCapBlock(World w)
        {
            var cap = w.C.Rules.Caps.Mines;
            return w.S.Objects.Count(o => o.Kind == ObjectKind.Mine) >= cap ? $"Mine cap reached ({cap})" : null;
        }

        private static List<TargetOption> EmptyTiles(World w, Unit p, int range)
        {
            return TilesWithin(w, p.Pos, range).Where(w.IsEmpty).Select(t => new TargetOption(t)).ToList();
        }

        private static readonly AbilityImpl ProximityMine = new AbilityImpl
        {
            Blocked = MineCapBlock,
            Options = (w, p) => MineCapBlock(w) != null ? new List<TargetOption>() : EmptyTiles(w, p, w.C.Abilities["proximity_mine"].Targeting.Range!.Value),
            Resolve = (w, p, opt) => w.PlaceObject(ObjectKind.Mine, opt.Target, new BoardObject { Team = Team.Player }, w.UnitSource(p, "proximity_mine")),
        };

        private static string? MinionCapBlock(World w)
        {
            var cap = w.C.Rules.Caps.Minions;
            return w.MinionCount() >= cap ? $"Minion cap reached ({cap})" : null;
        }

        private static readonly AbilityImpl AutoTurret = new AbilityImpl
        {
            Blocked = MinionCapBlock,
            Options = (w, p) => MinionCapBlock(w) != null ? new List<TargetOption>() : EmptyTiles(w, p, w.C.Abilities["auto_turret"].Targeting.Range!.Value),
            Resolve = (w, _, opt) => w.SpawnUnit(w.C.Abilities["auto_turret"].Summon!, opt.Target, "auto_turret"),
        };

        private static readonly AbilityImpl ScoutDrone = new AbilityImpl
        {
            Blocked = MinionCapBlock,
            Options = (w, p) => MinionCapBlock(w) != null ? new List<TargetOption>() : w.B.Neighbors8(p.Pos).Where(t => w.IsFree(t)).Select(t => new TargetOption(t)).ToList(),
            Resolve = (w, _, opt) => w.SpawnUnit(w.C.Abilities["scout_drone"].Summon!, opt.Target, "scout_drone"),
        };

        private static int ActiveShieldPlacements(World w)
        {
            return w.S.Objects.Where(o => o.Kind == ObjectKind.Shield).Select(o => o.Placement).Distinct().Count();
        }

        private static string? ShieldCapBlock(World w)
        {
            var cap = w.C.Rules.Caps.ShieldPlacements;
            return ActiveShieldPlacements(w) >= cap ? $"Shield cap reached ({cap} placements)" : null;
        }

        private static readonly AbilityImpl BarrierShield = new AbilityImpl
        {
            Blocked = ShieldCapBlock,
            Options = (w, p) =>
            {
                if (ShieldCapBlock(w) != null) return new List<TargetOption>();
                var tiles = TilesWithin(w, p.Pos, w.C.Abilities["barrier_shield"].Targeting.Range!.Value).Where(w.IsEmpty).ToList();
                var output = new List<TargetOption>();
                for (var i = 0; i < tiles.Count; i++)
                {
                    for (var j = i + 1; j < tiles.Count; j++)
                    {
                        if (Grid.Cheb(tiles[i], tiles[j]) == 1) output.Add(new TargetOption(tiles[i], tiles[j]));
                    }
                }
                return output;
            },
            Resolve = (w, p, opt) =>
            {
                var hp = w.C.Abilities["barrier_shield"].SegmentHp!.Value;
                var placement = w.S.NextPlacementId++;
                var src = w.UnitSource(p, "barrier_shield");
                foreach (var t in new[] { opt.Target, opt.Target2!.Value })
                {
                    w.PlaceObject(ObjectKind.Shield, t, new BoardObject { Team = Team.Player, Hp = hp, MaxHp = hp, Placement = placement }, src);
                }
            },
        };

        /// <summary>Lunge landings: up to 3 tiles in a straight orthogonal line, over units, not over terrain or shields and eggs.</summary>
        public static List<Pos> LungeLandings(World w, Unit p)
        {
            var range = w.C.Abilities["lunge"].Targeting.Range!.Value;
            var output = new List<Pos>();
            foreach (var d in Grid.Dirs4)
            {
                for (var k = 1; k <= range; k++)
                {
                    var t = Grid.Add(p.Pos, d, k);
                    if (w.B.BlocksMove(t) || w.BlockingObjectAt(t) != null) break;
                    if (w.UnitAt(t) != null) continue;
                    output.Add(t);
                }
            }
            return output;
        }

        private static readonly AbilityImpl Lunge = new AbilityImpl
        {
            Options = (w, p) =>
            {
                var output = new List<TargetOption>();
                foreach (var land in LungeLandings(w, p))
                {
                    output.Add(new TargetOption(land));
                    foreach (var u in w.ActiveUnits())
                    {
                        if (u.Id != p.Id && Grid.Cheb(u.Pos, land) == 1) output.Add(new TargetOption(land, u.Pos));
                    }
                }
                return output;
            },
            Resolve = (w, p, opt) =>
            {
                var def = w.C.Abilities["lunge"];
                if (!w.MoveAlong(p, new[] { opt.Target }, MoveMode.Lunge)) return;
                if (!opt.Target2.HasValue) return;
                var u = w.UnitAt(opt.Target2.Value);
                if (u != null && u.Id != p.Id && Grid.Cheb(u.Pos, p.Pos) == 1) w.DamageUnit(u, def.Damage!.Value, def.DamageType!.Value, w.UnitSource(p, "lunge"));
            },
        };

        private static readonly AbilityImpl SporePod = new AbilityImpl
        {
            Options = (w, p) =>
            {
                var def = w.C.Abilities["spore_pod"];
                var output = new List<TargetOption>();
                foreach (var t in TilesWithin(w, p.Pos, def.Targeting.Range!.Value))
                {
                    var area = w.B.Square(t, def.Area!.Radius);
                    if (w.ActiveUnits().Any(u => area.Contains(u.Pos))) output.Add(new TargetOption(t));
                }
                return output;
            },
            Resolve = (w, p, opt) =>
            {
                var def = w.C.Abilities["spore_pod"];
                var tiles = w.B.Square(opt.Target, def.Area!.Radius);
                var src = w.UnitSource(p, "spore_pod");
                w.Emit(new AreaEffect { Kind = AreaKind.Spore, Tiles = tiles, Source = src });
                foreach (var u in Sequences.StableSorted(w.ActiveUnits().Where(x => tiles.Contains(x.Pos)), (a, b) => a.Id - b.Id))
                {
                    w.ApplyStatus(u, def.Status!.Id, def.Status!.Duration, src);
                }
            },
        };

        /// <summary>What an egg on this tile would hatch into, or null if the tile cannot take an egg.</summary>
        public static EggPlanResult? EggPlan(World w, Pos t)
        {
            var eggs = w.C.Rules.Eggs;
            if (w.IsEmpty(t)) return new EggPlanResult { Host = "floor", HatchInto = eggs.FloorVariant, Timer = eggs.FloorTimer, Count = eggs.FloorHatchlings };
            var objs = w.ObjectsAt(t);
            var corpse = objs.FirstOrDefault(o => o.Kind == ObjectKind.Corpse);
            if (corpse == null || objs.Count != 1 || w.UnitAt(t) != null || w.B.BlocksMove(t)) return null;
            if (!eggs.CorpseHosts.TryGetValue(corpse.Of!, out var variant) || string.IsNullOrEmpty(variant)) return null;
            var timer = w.HasUp(Up.A4) ? w.UpDef(Up.A4).CorpseTimer ?? eggs.CorpseTimer : eggs.CorpseTimer;
            return new EggPlanResult { Host = corpse.Of!, HatchInto = variant!, Timer = timer, Count = 1, CorpseId = corpse.Id };
        }

        private static string? EggCapBlock(World w)
        {
            var cap = w.C.Rules.Caps.Eggs;
            return w.S.Objects.Count(o => o.Kind == ObjectKind.Egg) >= cap ? $"Egg cap reached ({cap})" : null;
        }

        private static readonly AbilityImpl BroodEgg = new AbilityImpl
        {
            Blocked = EggCapBlock,
            Options = (w, p) =>
            {
                if (EggCapBlock(w) != null) return new List<TargetOption>();
                return TilesWithin(w, p.Pos, w.C.Abilities["brood_egg"].Targeting.Range!.Value).Where(t => EggPlan(w, t) != null).Select(t => new TargetOption(t)).ToList();
            },
            Resolve = (w, p, opt) =>
            {
                var plan = EggPlan(w, opt.Target)!;
                if (plan.CorpseId.HasValue)
                {
                    var corpse = w.S.Objects.First(o => o.Id == plan.CorpseId.Value);
                    w.RemoveObject(corpse, RemovalReason.Consumed);
                }
                var hp = w.C.Rules.Eggs.Hp;
                w.PlaceObject(ObjectKind.Egg, opt.Target, new BoardObject { Team = Team.Player, Hp = hp, MaxHp = hp, Timer = plan.Timer, Host = plan.Host, HatchInto = plan.HatchInto }, w.UnitSource(p, "brood_egg"));
            },
        };

        public static readonly IReadOnlyDictionary<string, AbilityImpl> All = new Dictionary<string, AbilityImpl>(StringComparer.Ordinal)
        {
            { "sidearm", Sidearm },
            { "grapple_hook", Grapple },
            { "proximity_mine", ProximityMine },
            { "auto_turret", AutoTurret },
            { "scout_drone", ScoutDrone },
            { "barrier_shield", BarrierShield },
            { "acid_spit", AcidSpit },
            { "lunge", Lunge },
            { "spore_pod", SporePod },
            { "brood_egg", BroodEgg },
            { "parasite", Parasite },
        };

        public static bool SameOption(TargetOption a, TargetOption b)
        {
            if (a.Target != b.Target) return false;
            if (a.Mode != b.Mode) return false;
            if (!a.Target2.HasValue || !b.Target2.HasValue) return a.Target2.HasValue == b.Target2.HasValue;
            return a.Target2.Value == b.Target2.Value;
        }

        /// <summary>Barrier Shield pairs are unordered: either tile may be given first.</summary>
        public static TargetOption? MatchOption(IEnumerable<TargetOption> options, TargetOption want)
        {
            return options.FirstOrDefault(o => SameOption(o, want)
                || (o.Target2.HasValue && want.Target2.HasValue && o.Target == want.Target2.Value && o.Target2.Value == want.Target && o.Mode == want.Mode));
        }
    }
}
