#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public sealed class ShotPayload
    {
        public SourceRef Source = null!;
        public int? Damage;
        public DamageType? DamageType;
        public int? ArmorIgnore;
        public int? Corrode;
        public StatusDef? Status;
        /// <summary>Runs after the hit lands on a unit that survives it (Suppressing Fire).</summary>
        public Action<Unit>? OnHitUnit;
        /// <summary>Sidearm with Shot-Triggered Mines aimed at this mine tile. Other shots pass over mines.</summary>
        public Pos? MineTarget;
        /// <summary>Damaging shots can be reflected by a Reflective Shield. The grapple cannot.</summary>
        public bool Reflectable;
    }

    public sealed class Trace
    {
        /// <summary>Tiles the projectile crossed, ending with the tile it stopped on.</summary>
        public List<Pos> Path = new List<Pos>();
        public Unit? HitUnit;
        public BoardObject? HitObject;
        public BoardObject? Mine;
        /// <summary>Stopped by terrain that blocks projectiles (shutter, wall gun, pillar, wall).</summary>
        public bool Blocked;
        public Pos? HitPos;
    }

    public static class Projectiles
    {
        /// <summary>The tiles a projectile crosses from `from` toward `to`, excluding the shooter's own tile.</summary>
        public static List<Pos> ShotPath(Pos from, Pos to) => Grid.LineTiles(from, to).Skip(1).ToList();

        /// <summary>Walks a projectile path and finds the first unit, blocking object, blocking terrain or aimed mine.</summary>
        public static Trace TraceProjectile(World w, IEnumerable<Pos> path, Pos? mineTarget = null, int? ignoreUnit = null)
        {
            var crossed = new List<Pos>();
            foreach (var p in path)
            {
                crossed.Add(p);
                var u = w.UnitAt(p);
                if (u != null && u.Id != ignoreUnit) return new Trace { Path = crossed, HitUnit = u, HitPos = p };
                var o = w.BlockingObjectAt(p);
                if (o != null) return new Trace { Path = crossed, HitObject = o, HitPos = p };
                if (w.B.BlocksProjectile(p)) return new Trace { Path = crossed, Blocked = true, HitPos = p };
                if (mineTarget.HasValue && p == mineTarget.Value)
                {
                    var m = w.MineAt(p);
                    if (m != null) return new Trace { Path = crossed, Mine = m, HitPos = p };
                }
            }
            return new Trace { Path = crossed, HitPos = null };
        }

        public static EntityRef? TraceHitRef(Trace t)
        {
            if (t.HitUnit != null) return EntityRef.Unit(t.HitUnit.Id);
            if (t.HitObject != null) return EntityRef.Object(t.HitObject.Id);
            if (t.Mine != null) return EntityRef.Object(t.Mine.Id);
            return null;
        }

        /// <summary>
        /// Fires a projectile along a path and applies what it hits: the first unit or blocking object, friendly or not. A shield
        /// segment with Reflective Shield sends the first projectile each round back along its line.
        /// </summary>
        public static Trace FireProjectile(World w, Pos from, IReadOnlyList<Pos> path, ShotPayload payload, bool reflected = false)
        {
            var tr = TraceProjectile(w, path, reflected ? (Pos?)null : payload.MineTarget);
            w.Emit(new ProjectileFired { Source = payload.Source, From = from, Path = tr.Path, Hit = TraceHitRef(tr), HitPos = tr.HitPos, Reflected = reflected });
            if (tr.Mine != null)
            {
                w.TriggerMine(tr.Mine, null);
                return tr;
            }
            var o = tr.HitObject;
            if (o != null)
            {
                if (o.Kind == ObjectKind.Shield && payload.Reflectable && w.HasUp(Up.T5) && o.ReflectedRound != w.S.Round)
                {
                    var back = Sequences.Reversed(tr.Path.Take(tr.Path.Count - 1).ToList());
                    back.Add(from);
                    w.Immediate("o" + o.Id, Up.T5, () =>
                    {
                        o.ReflectedRound = w.S.Round;
                        FireProjectile(w, o.Pos, back, payload, true);
                    });
                    return tr;
                }
                if (payload.Damage.GetValueOrDefault() != 0) w.DamageObject(o, payload.Damage!.Value, payload.DamageType!.Value, payload.Source);
                return tr;
            }
            if (tr.HitUnit != null) ApplyShotToUnit(w, tr.HitUnit, payload);
            return tr;
        }

        private static void ApplyShotToUnit(World w, Unit u, ShotPayload payload)
        {
            if (payload.Damage.GetValueOrDefault() != 0) w.DamageUnit(u, payload.Damage!.Value, payload.DamageType!.Value, payload.Source, payload.ArmorIgnore ?? 0);
            if (u.Dead) return;
            if (payload.Corrode.GetValueOrDefault() != 0) w.ApplyStatus(u, StatusId.Corrode, payload.Corrode!.Value, payload.Source);
            if (payload.Status != null) w.ApplyStatus(u, payload.Status.Id, payload.Status.Duration, payload.Source);
            payload.OnHitUnit?.Invoke(u);
        }
    }
}
