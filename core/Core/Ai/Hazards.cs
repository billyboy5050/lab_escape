#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public sealed class HazardTargets
    {
        /// <summary>Units in the area, or for the wall gun the first unit in its lane.</summary>
        public List<Unit> Units = new List<Unit>();
        /// <summary>Wall gun only: the first blocking object in its lane, when it comes before any unit.</summary>
        public BoardObject? Object;
        /// <summary>Tiles the hazard covers (for the gun, the lane up to what it hits).</summary>
        public List<Pos> Tiles = new List<Pos>();
    }

    public sealed class Classified
    {
        public bool Elite;
        public bool Player;
        public bool Soldier;
        public bool PlayerSide;
        public List<EntityRef> Victims = new List<EntityRef>();
    }

    public sealed class PrimeDecision
    {
        public Hazard Hazard = null!;
        public bool Prime;
        public int Step;
        public string Reason = "";
        public List<EntityRef> Victims = new List<EntityRef>();
    }

    public static class HazardAi
    {
        /// <summary>The full lane of a lane hazard, from the tile after the gun to the edge of the map.</summary>
        public static List<Pos> LaneTiles(World w, Hazard hz)
        {
            var output = new List<Pos>();
            var p = hz.Tiles[0];
            for (; ; )
            {
                p = Grid.Add(p, hz.Dir!.Value);
                if (!w.B.InBounds(p)) break;
                output.Add(p);
            }
            return output;
        }

        /// <summary>What a hazard would hit if it fired now. Panels and vents count units only; the gun hits the first thing in its lane.</summary>
        public static HazardTargets Targets(World w, Hazard hz)
        {
            var def = w.C.HazardDefs[hz.Def];
            if (def.Shape == HazardShape.Lane)
            {
                var lane = new List<Pos>();
                foreach (var p in LaneTiles(w, hz))
                {
                    lane.Add(p);
                    var u = w.UnitAt(p);
                    if (u != null) return new HazardTargets { Units = new List<Unit> { u }, Tiles = lane };
                    var o = w.BlockingObjectAt(p);
                    if (o != null) return new HazardTargets { Object = o, Tiles = lane };
                    if (w.B.BlocksProjectile(p)) break;
                }
                return new HazardTargets { Tiles = lane };
            }
            var tiles = new List<Pos>();
            foreach (var t in hz.Tiles)
            {
                foreach (var p in w.B.Square(t, def.Radius!.Value))
                {
                    if (!tiles.Contains(p)) tiles.Add(p);
                }
            }
            tiles = Sequences.StableSorted(tiles, (a, b) => a.Y != b.Y ? a.Y - b.Y : a.X - b.X);
            var covered = tiles;
            var units = Sequences.StableSorted(w.ActiveUnits().Where(u => covered.Contains(u.Pos)), (a, b) => a.Id - b.Id);
            return new HazardTargets { Units = units, Tiles = covered };
        }

        /// <summary>
        /// The lab ranks targets elite, then the player's life, then soldier, then minion. A soldier is any enemy that is not an
        /// elite. A player-side object first in the gun's lane (shield segment or egg) ranks as a minion.
        /// </summary>
        public static Classified Classify(HazardTargets tg)
        {
            var c = new Classified();
            foreach (var u in tg.Units)
            {
                c.Victims.Add(EntityRef.Unit(u.Id));
                if (u.Team == Team.Enemy)
                {
                    if (u.Elite) c.Elite = true;
                    else c.Soldier = true;
                }
                else
                {
                    c.PlayerSide = true;
                    if (u.Id == 0) c.Player = true;
                }
            }
            if (tg.Object != null)
            {
                c.Victims.Add(EntityRef.Object(tg.Object.Id));
                if (tg.Object.Team == Team.Player) c.PlayerSide = true;
            }
            return c;
        }

        public static SourceRef SourceOf(Hazard hz) => new SourceRef(SourceSide.Hazard, SourceCategory.Hazard, SourceKind.Hazard, hz.Id, hz.Def, hz.Def);

        /// <summary>Damage a hazard would deal to the player, after armor.</summary>
        public static int DamageToPlayer(World w, Hazard hz)
        {
            var def = w.C.HazardDefs[hz.Def];
            var p = w.Player;
            if (p == null) return 0;
            var armor = w.C.Rules.ArmorAppliesTo.Contains(def.DamageType) ? w.ArmorOf(p) : 0;
            return Math.Max(0, def.Damage - armor);
        }

        /// <summary>
        /// The value rule, evaluated for every ready hazard at once. The combined kill check is computed once per priming step,
        /// over ready hazards that would hit the player and no elite, in the fixed hazard order. Pure: it reads the state and
        /// changes nothing.
        /// </summary>
        public static List<PrimeDecision> PrimeDecisions(World w)
        {
            var ready = w.S.Hazards.Where(h => h.State == HazardState.Ready).ToList();
            var info = ready.Select(h => new KeyValuePair<Hazard, Classified>(h, Classify(Targets(w, h)))).ToList();
            var player = w.Player;
            var kill = info.Where(i => i.Value.Player && !i.Value.Elite).ToList();
            var total = kill.Sum(i => DamageToPlayer(w, i.Key));
            var combined = player != null && kill.Count > 0 && total >= player.Hp;
            return info.Select(pair =>
            {
                var h = pair.Key;
                var cls = pair.Value;
                var d = new PrimeDecision { Hazard = h, Victims = cls.Victims };
                if (cls.Elite)
                {
                    d.Prime = false;
                    d.Step = 1;
                    d.Reason = "An elite would be hit";
                }
                else if (cls.Player && combined)
                {
                    d.Prime = true;
                    d.Step = 2;
                    d.Reason = $"Combined kill: {total} damage against {player!.Hp} HP";
                }
                else if (!cls.Soldier && cls.PlayerSide)
                {
                    d.Prime = true;
                    d.Step = 3;
                    d.Reason = "Only your side would be hit";
                }
                else
                {
                    d.Prime = false;
                    d.Step = 4;
                    d.Reason = cls.Soldier ? "A soldier would be hit" : "Nothing to hit";
                }
                return d;
            }).ToList();
        }

        private static void Record(World w, Hazard hz, HazardStage stage, HazardResult result, int? step, string reason, List<EntityRef> victims)
        {
            var full = new HazardDecision { Round = w.S.Round, Stage = stage, Result = result, Step = step, Reason = reason, Victims = victims };
            hz.Last = full;
            hz.History.Add(full);
            if (hz.History.Count > 12) hz.History.RemoveAt(0);
        }

        /// <summary>Environment step 2: ready hazards check the value rule and prime (showing their cue) or hold.</summary>
        public static void PrimeStep(World w)
        {
            foreach (var d in PrimeDecisions(w))
            {
                var hz = d.Hazard;
                Record(w, hz, HazardStage.Prime, d.Prime ? HazardResult.Prime : HazardResult.Hold, d.Step, d.Reason, d.Victims);
                if (d.Prime)
                {
                    hz.State = HazardState.Primed;
                    w.Emit(new HazardPrimed { Hazard = hz.Id, Step = d.Step, Reason = d.Reason, Victims = d.Victims });
                    IntercomRules.Trigger(w, TriggerTypes.HazardPrimed);
                }
                else
                {
                    w.Emit(new HazardHeld { Hazard = hz.Id, Step = d.Step, Reason = d.Reason, Victims = d.Victims });
                }
            }
        }

        /// <summary>
        /// Environment step 1: a primed hazard is committed. It fires on everything then in its area, soldiers included, and goes
        /// on cooldown. It cancels, with a short cooldown, only if an elite would be hit or nothing would be hit.
        /// </summary>
        public static void FireStep(World w)
        {
            foreach (var hz in w.S.Hazards)
            {
                if (hz.State != HazardState.Primed) continue;
                var def = w.C.HazardDefs[hz.Def];
                var tg = Targets(w, hz);
                var cls = Classify(tg);
                if (cls.Elite || cls.Victims.Count == 0)
                {
                    var reason = cls.Elite ? "An elite would be hit" : "Nothing to hit";
                    hz.State = HazardState.Cooldown;
                    hz.Cooldown = def.CancelCooldown;
                    Record(w, hz, HazardStage.Fire, HazardResult.Cancel, null, reason, cls.Victims);
                    w.Emit(new HazardCancelled { Hazard = hz.Id, Reason = reason, Victims = cls.Victims });
                    continue;
                }
                hz.State = HazardState.Cooldown;
                hz.Cooldown = def.Cooldown;
                Record(w, hz, HazardStage.Fire, HazardResult.Fire, null, "Fired", cls.Victims);
                w.Emit(new HazardFired { Hazard = hz.Id, Victims = cls.Victims, Tiles = tg.Tiles });
                var src = SourceOf(hz);
                if (def.Shape == HazardShape.Lane)
                {
                    Projectiles.FireProjectile(w, hz.Tiles[0], LaneTiles(w, hz), new ShotPayload { Source = src, Damage = def.Damage, DamageType = def.DamageType, Reflectable = true });
                }
                else
                {
                    w.Emit(new AreaEffect { Kind = def.DamageType == DamageType.Electric ? AreaKind.Panel : AreaKind.Gas, Tiles = tg.Tiles, Source = src });
                    foreach (var u in tg.Units) w.DamageUnit(u, def.Damage, def.DamageType, src);
                }
                if (cls.Player) IntercomRules.Trigger(w, TriggerTypes.HazardHitsPlayer);
                if (cls.Soldier) IntercomRules.Trigger(w, TriggerTypes.HazardHitsSoldier);
                w.FlushDeaths();
            }
        }
    }
}
