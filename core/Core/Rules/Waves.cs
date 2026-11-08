#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace LabEscape.Core
{
    public static class Waves
    {
        /// <summary>Number of waves in play: all of them unless a debug setting holds later waves back.</summary>
        public static int InPlay(World w) => Math.Min(w.S.Settings.MaxWaves ?? w.C.Waves.Count, w.C.Waves.Count);

        /// <summary>
        /// Round start: spawn every wave scheduled for this round or earlier. With earlyWaveWhenEmpty, an empty room also brings
        /// the next wave forward (one wave per round).
        /// </summary>
        public static void SpawnDue(World w)
        {
            var inPlay = InPlay(w);
            while (w.S.WavesSpawned < inPlay)
            {
                var next = w.C.Waves[w.S.WavesSpawned];
                var due = next.Round <= w.S.Round;
                var early = !due && w.C.Rules.EarlyWaveWhenEmpty && w.S.WavesSpawned > 0 && w.Enemies().Count == 0;
                if (!due && !early) break;
                Spawn(w, w.S.WavesSpawned);
                if (early) break;
            }
        }

        public static bool ArrivalApplies(World w, int waveIndex)
        {
            var mode = w.C.Rules.ArrivalRound;
            return mode == ArrivalRound.All || (mode == ArrivalRound.First && waveIndex == 0);
        }

        /// <summary>
        /// Spawns a wave. Units take IDs in table order. An occupied spawn tile moves the unit to the nearest free tile. Every
        /// unit is placed before any mine under one goes off.
        /// </summary>
        public static List<Unit> Spawn(World w, int index)
        {
            var wave = w.C.Waves[index];
            var arrival = ArrivalApplies(w, index);
            var units = new List<Unit>();
            foreach (var entry in wave.Units)
            {
                var tile = w.FindSpawnTile(Tiles.Parse(entry.Tile));
                if (!tile.HasValue) continue;
                units.Add(w.SpawnUnit(entry.Def, tile.Value, "wave " + (index + 1), arrival: arrival, triggerMines: false));
            }
            w.S.WavesSpawned = Math.Max(w.S.WavesSpawned, index + 1);
            w.Emit(new WaveSpawned { Wave = index + 1, Units = units.Select(u => u.Id).ToList() });
            foreach (var u in units) w.TriggerMineUnder(u);
            return units;
        }
    }
}
