#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using LabEscape.Core;

namespace LabEscape.Client
{
    /// <summary>
    /// What the debug overlay's edit controls hold: the unit picked, the HP, AP and movement to set, and the kind of unit to
    /// spawn. It has no Unity code. The controls are steppers and a click on the board, never text fields, because a focused
    /// text field would take the game's keys (E, Space, 1 to =) while the player typed. Each edit is a debug command, so it is
    /// recorded in the fight's replay and runs through the engine like any other.
    /// </summary>
    public sealed class DebugEdits
    {
        public const int MaxHp = 99;
        public const int MaxAp = 20;

        /// <summary>The unit whose HP is set. -1 until <see cref="Sync"/> has chosen one.</summary>
        public int UnitId { get; private set; } = -1;

        public int Hp { get; private set; } = 1;

        /// <summary>What Set AP and movement would set. A new fight starts them at a turn's own AP and movement, from the rules.</summary>
        public int Ap { get; private set; }

        public int Move { get; private set; }

        public string SpawnDef { get; private set; } = "guard";

        /// <summary>The kind of unit the next click on the board spawns, or null when no spawn is waiting for a tile.</summary>
        public string? Armed { get; set; }

        /// <summary>A new fight: the unit picked belonged to the last one, a spawn that was waiting for a tile is dropped, and AP and movement start at a turn's.</summary>
        public void Reset(Content c)
        {
            UnitId = -1;
            Armed = null;
            Ap = Math.Min(MaxAp, c.Rules.Turn.Ap);
            Move = Math.Min(MaxAp, c.Rules.Turn.Movement);
        }

        /// <summary>Everything still in the fight, the player first: the units whose HP can be set.</summary>
        public static List<Unit> Units(GameState s) => s.Units.Where(u => !u.Dead).ToList();

        /// <summary>Everything that can be spawned: every unit definition but the player's.</summary>
        public static List<string> SpawnDefs(Content c) => c.Units.Select(p => p.Key).Where(d => d != "player").ToList();

        /// <summary>
        /// Keeps the picks valid as the fight and the content change: the unit picked is one that is still in the fight, and the
        /// kind to spawn is one the content has. A new unit picked starts with its own HP.
        /// </summary>
        public void Sync(Content c, GameState s)
        {
            var units = Units(s);
            if (units.Count > 0 && units.All(u => u.Id != UnitId)) Select(units[0]);
            var defs = SpawnDefs(c);
            if (defs.Count > 0 && !defs.Contains(SpawnDef)) SpawnDef = defs[0];
        }

        private void Select(Unit u)
        {
            UnitId = u.Id;
            Hp = Math.Max(1, Math.Min(MaxHp, u.Hp));
        }

        /// <summary>The next (`direction` 1) or previous (-1) unit, wrapping round.</summary>
        public void CycleUnit(GameState s, int direction)
        {
            var units = Units(s);
            if (units.Count == 0) return;
            var at = units.FindIndex(u => u.Id == UnitId);
            Select(units[at < 0 ? 0 : (at + direction + units.Count) % units.Count]);
        }

        public void CycleSpawn(Content c, int direction)
        {
            var defs = SpawnDefs(c);
            if (defs.Count == 0) return;
            var at = defs.IndexOf(SpawnDef);
            SpawnDef = defs[at < 0 ? 0 : (at + direction + defs.Count) % defs.Count];
            // A spawn waiting for a tile follows the kind picked.
            if (Armed != null) Armed = SpawnDef;
        }

        public void StepHp(int by) => Hp = Math.Max(1, Math.Min(MaxHp, Hp + by));

        public void StepAp(int by) => Ap = Math.Max(0, Math.Min(MaxAp, Ap + by));

        public void StepMove(int by) => Move = Math.Max(0, Math.Min(MaxAp, Move + by));

        public Command SetHp() => new DebugSetHp(UnitId, Hp);

        public Command SetApAndMove() => new DebugSetAp(Ap, Move);

        public Command Spawn(string def, Pos tile) => new DebugSpawn(def, Tiles.Name(tile));

        public Command ForceWave() => new DebugForceWave();

        /// <summary>A waiting wave: the fight is not yet in its last.</summary>
        public static bool CanForceWave(Content c, GameState s) => s.WavesSpawned < c.RoomFor(s.Settings.Room).Waves.Count;

        /// <summary>"#3 Guard", as the web panel's list names a unit.</summary>
        public static string UnitLabel(Content c, GameState s, int id)
        {
            var u = s.Units.FirstOrDefault(x => x.Id == id);
            return u == null ? "none" : $"#{u.Id} {Words.UnitName(c, u.Def)}";
        }

        public static string SpawnName(Content c, string def) => c.Units.TryGetValue(def, out var d) ? d.Name : def;
    }
}
