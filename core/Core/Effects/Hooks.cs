#nullable enable
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public static class Hooks
    {
        /// <summary>
        /// Death hooks, registered in a fixed order: upgrade hooks in upgrade-table order, then the base Parasite rule. A
        /// hatchling bursting from a host therefore appears after the host's Spore Burst, so the burst does not poison it.
        /// </summary>
        public static void Register(World w)
        {
            if (w.HasUp(Up.A1)) w.DeathHooks.Add(SporeBurst);
            if (w.HasUp(Up.A2)) w.DeathHooks.Add(AcidBrood);
            if (w.HasUp(Up.A3)) w.DeathHooks.Add(ParasiteJump);
            w.DeathHooks.Add(ParasiteHatch);
        }

        /// <summary>A1 Spore Burst: when a poisoned unit dies, it poisons its 3x3 area.</summary>
        public static readonly DeathHook SporeBurst = (w, dead) =>
        {
            if (dead.Statuses.Poison == null) return;
            var pos = dead.Pos;
            w.Enqueue(Up.A1, "u" + dead.Id, () =>
            {
                var up = w.UpDef(Up.A1);
                var tiles = w.B.Square(pos, up.Area!.Radius);
                var src = w.PlayerEffect(Up.A1, SourceKind.Unit, dead.Id, dead.Def);
                w.Emit(new AreaEffect { Kind = AreaKind.Burst, Tiles = tiles, Source = src });
                foreach (var u in UnitsOn(w, tiles)) w.ApplyStatus(u, up.Status!.Id, up.Status!.Duration, src);
            });
        };

        /// <summary>A2 Acid Brood: when any hatchling dies, adjacent units gain Corrode.</summary>
        public static readonly DeathHook AcidBrood = (w, dead) =>
        {
            if (!w.C.Units.TryGetValue(dead.Def, out var def) || !def.Hatchling) return;
            var pos = dead.Pos;
            w.Enqueue(Up.A2, "u" + dead.Id, () =>
            {
                var up = w.UpDef(Up.A2);
                var src = w.PlayerEffect(Up.A2, SourceKind.Unit, dead.Id, dead.Def);
                foreach (var u in Sequences.StableSorted(w.ActiveUnits().Where(x => Grid.Cheb(x.Pos, pos) == 1), (a, b) => a.Id - b.Id))
                {
                    w.ApplyStatus(u, StatusId.Corrode, up.Corrode ?? 0, src);
                }
            });
        };

        /// <summary>A3 Parasite Jump: when a Parasite host dies, the Parasite jumps to the nearest enemy within range and sight.</summary>
        public static readonly DeathHook ParasiteJump = (w, dead) =>
        {
            var para = dead.Statuses.Parasite;
            if (para == null) return;
            var pos = dead.Pos;
            w.Enqueue(Up.A3, "u" + dead.Id, () =>
            {
                var up = w.UpDef(Up.A3);
                var range = up.Range!.Value;
                var cands = w.Enemies().Where(u => u.Id != dead.Id && Grid.Cheb(u.Pos, pos) <= range && w.B.HasLos(pos, u.Pos)).ToList();
                var target = Pathing.MinBy(cands, u => new[] { Grid.Cheb(u.Pos, pos), u.Hp, u.Id });
                if (target != null) w.ApplyStatus(target, StatusId.Parasite, up.Status?.Duration ?? w.C.Rules.Statuses.Parasite.Duration, para.Src.WithVia(Up.A3));
            });
        };

        /// <summary>Base rule: when a Parasite host dies, a basic hatchling spawns on its tile if the minion cap allows.</summary>
        public static readonly DeathHook ParasiteHatch = (w, dead) =>
        {
            if (dead.Statuses.Parasite == null) return;
            var pos = dead.Pos;
            w.Enqueue("parasite_hatch", "u" + dead.Id, () =>
            {
                if (w.MinionCount() >= w.C.Rules.Caps.Minions) return;
                var tile = w.IsFree(pos) ? pos : w.FindSpawnTile(pos);
                if (tile.HasValue) w.SpawnUnit(w.C.Rules.ParasiteHatchling, tile.Value, "parasite");
            });
        };

        private static List<Unit> UnitsOn(World w, List<Pos> tiles)
        {
            return Sequences.StableSorted(w.ActiveUnits().Where(u => tiles.Contains(u.Pos)), (a, b) => a.Id - b.Id);
        }
    }
}
