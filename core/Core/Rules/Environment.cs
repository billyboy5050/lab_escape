#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public static class EnvironmentRules
    {
        private static readonly StatusId[] Timed = { StatusId.Poison, StatusId.Parasite, StatusId.Pinned, StatusId.Slowed, StatusId.Corrode };

        /// <summary>
        /// Environment step 3. Every status present at the start of the step ticks once, in unit ID order: poison and Parasite
        /// deal damage, and every timed status counts down. Poison then spreads. Effects set off by deaths during the step
        /// (Spore Burst, Parasite) run after the spread, so anything they apply first ticks next round.
        /// </summary>
        public static void StatusTickStep(World w)
        {
            var r = w.C.Rules.Statuses;
            var order = Sequences.StableSorted(w.ActiveUnits(), (a, b) => a.Id - b.Id);
            var eligible = new Dictionary<int, StatusId[]>();
            foreach (var u in order) eligible[u.Id] = Timed.Where(u.Statuses.Has).ToArray();
            foreach (var u in order)
            {
                if (u.Dead) continue;
                var ks = eligible[u.Id];
                // A unit that dies keeps its statuses, so death hooks (Spore Burst, Parasite) can see them.
                if (ks.Contains(StatusId.Poison) && u.Statuses.Poison != null)
                {
                    w.DamageUnit(u, r.Poison.Damage, r.Poison.DamageType, u.Statuses.Poison.Src.WithVia("poison"));
                    if (!u.Dead) CountDown(w, u, StatusId.Poison);
                }
                if (ks.Contains(StatusId.Parasite) && u.Statuses.Parasite != null && !u.Dead)
                {
                    w.DamageUnit(u, r.Parasite.Damage, r.Parasite.DamageType, u.Statuses.Parasite.Src.WithVia("parasite"));
                    if (!u.Dead) CountDown(w, u, StatusId.Parasite);
                }
                if (!u.Dead)
                {
                    if (ks.Contains(StatusId.Pinned)) CountDown(w, u, StatusId.Pinned);
                    if (ks.Contains(StatusId.Slowed)) CountDown(w, u, StatusId.Slowed);
                    if (ks.Contains(StatusId.Corrode) && u.Statuses.Corrode != null && u.Statuses.Corrode.Remaining.HasValue) CountDown(w, u, StatusId.Corrode);
                }
                w.FlushDeaths();
            }
            SpreadPoison(w);
        }

        private static void CountDown(World w, Unit u, StatusId id)
        {
            int remaining;
            if (id == StatusId.Corrode)
            {
                var corrode = u.Statuses.Corrode;
                if (corrode == null || !corrode.Remaining.HasValue) return;
                corrode.Remaining -= 1;
                remaining = corrode.Remaining.Value;
            }
            else
            {
                var st = TimedOf(u.Statuses, id);
                if (st == null) return;
                st.Remaining -= 1;
                remaining = st.Remaining;
            }
            w.Emit(new StatusTick { Target = u.Id, Status = id, Remaining = remaining });
            if (remaining <= 0)
            {
                u.Statuses.Remove(id);
                w.Emit(new StatusExpired { Target = u.Id, Status = id });
            }
        }

        private static TimedStatus? TimedOf(Statuses st, StatusId id)
        {
            switch (id)
            {
                case StatusId.Poison: return st.Poison;
                case StatusId.Parasite: return st.Parasite;
                case StatusId.Pinned: return st.Pinned;
                case StatusId.Slowed: return st.Slowed;
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        private sealed class SpreadCandidate
        {
            public Unit U = null!;
            public int D;
            public SourceRef Src = null!;
        }

        /// <summary>
        /// After the tick, every unpoisoned unit next to a poisoned one is a candidate. Candidates are infected in order of the
        /// duration they would inherit (the best adjacent source's remaining rounds, highest first), then by unit ID, up to the
        /// spread cap. A Warden that purged this round is immune.
        /// </summary>
        public static void SpreadPoison(World w)
        {
            var cap = w.C.Rules.Statuses.Poison.SpreadCap;
            var units = w.ActiveUnits();
            var sources = units.Where(u => u.Statuses.Poison != null && u.Statuses.Poison.Remaining > 0).ToList();
            if (sources.Count == 0 || cap <= 0) return;
            var cands = new List<SpreadCandidate>();
            foreach (var u in units)
            {
                if (u.Statuses.Poison != null || u.PurgeImmuneRound == w.S.Round) continue;
                Unit? best = null;
                foreach (var s in sources)
                {
                    if (Grid.Cheb(s.Pos, u.Pos) != 1) continue;
                    if (best == null || s.Statuses.Poison!.Remaining > best.Statuses.Poison!.Remaining || (s.Statuses.Poison!.Remaining == best.Statuses.Poison!.Remaining && s.Id < best.Id)) best = s;
                }
                if (best != null) cands.Add(new SpreadCandidate { U = u, D = best.Statuses.Poison!.Remaining, Src = best.Statuses.Poison!.Src });
            }
            var ordered = Sequences.StableSorted(cands, (a, b) => b.D != a.D ? b.D - a.D : a.U.Id - b.U.Id);
            foreach (var c in ordered.Take(cap)) w.ApplyStatus(c.U, StatusId.Poison, c.D, c.Src, spread: true);
        }

        /// <summary>Environment step 4: egg timers count down, and eggs at 0 hatch.</summary>
        public static void EggStep(World w)
        {
            var eggs = Sequences.StableSorted(w.S.Objects.Where(o => o.Kind == ObjectKind.Egg && !o.Destroyed), (a, b) => a.Id - b.Id);
            foreach (var egg in eggs)
            {
                if (egg.Destroyed) continue;
                egg.Timer = (egg.Timer ?? 1) - 1;
                if (egg.Timer.Value <= 0) HatchEgg(w, egg);
            }
        }

        /// <summary>Hatches an egg. Hatchlings that would exceed the minion cap do not spawn, but the egg still hatches.</summary>
        public static void HatchEgg(World w, BoardObject egg)
        {
            w.RemoveObject(egg, RemovalReason.Hatched);
            var variant = egg.HatchInto!;
            var count = egg.Host == "floor" ? w.C.Rules.Eggs.FloorHatchlings : 1;
            var spawned = new List<Unit>();
            var capped = 0;
            for (var i = 0; i < count; i++)
            {
                if (w.MinionCount() >= w.C.Rules.Caps.Minions)
                {
                    capped++;
                    continue;
                }
                var tile = i == 0 && w.IsFree(egg.Pos) ? egg.Pos : NearestFreeAdjacent(w, egg.Pos);
                if (!tile.HasValue)
                {
                    capped++;
                    continue;
                }
                spawned.Add(w.SpawnUnit(variant, tile.Value, "egg", triggerMines: false));
            }
            w.Emit(new EggHatched { Id = egg.Id, Pos = egg.Pos, Host = egg.Host ?? "floor", Spawned = spawned.Select(u => u.Id).ToList(), Capped = capped });
            foreach (var u in spawned) w.TriggerMineUnder(u);
        }

        /// <summary>The first free tile among the 8 neighbours, lowest row then lowest column.</summary>
        public static Pos? NearestFreeAdjacent(World w, Pos p)
        {
            foreach (var d in Grid.Dirs8)
            {
                var q = Grid.Add(p, d);
                if (w.B.InBounds(q) && w.IsFree(q)) return q;
            }
            return null;
        }

        /// <summary>Environment step 5: hazard cooldowns, unit cooldowns (Purge) and corpse decay advance.</summary>
        public static void DecayStep(World w)
        {
            foreach (var h in w.S.Hazards)
            {
                if (h.State != HazardState.Cooldown) continue;
                h.Cooldown = Math.Max(0, h.Cooldown - 1);
                if (h.Cooldown == 0) h.State = HazardState.Ready;
            }
            foreach (var u in w.S.Units)
            {
                foreach (var k in u.Cooldowns.Keys.ToList())
                {
                    if (u.Cooldowns[k] > 0) u.Cooldowns[k] -= 1;
                }
            }
            var corpses = Sequences.StableSorted(w.S.Objects.Where(o => o.Kind == ObjectKind.Corpse), (a, b) => a.Id - b.Id);
            foreach (var c in corpses)
            {
                c.Decay = (c.Decay ?? 1) - 1;
                if (c.Decay.Value <= 0) w.RemoveObject(c, RemovalReason.Decayed);
            }
        }
    }
}
