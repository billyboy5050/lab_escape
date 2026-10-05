import { parseTile } from '../util/tiles';
import type { Unit } from '../state/types';
import type { World } from './world';

/** Number of waves in play: all of them unless a debug setting holds later waves back. */
export function wavesInPlay(w: World): number {
  return Math.min(w.s.settings.maxWaves ?? w.c.waves.length, w.c.waves.length);
}

/**
 * Round start: spawn every wave scheduled for this round or earlier. With earlyWaveWhenEmpty, an empty
 * room also brings the next wave forward (one wave per round).
 */
export function spawnDueWaves(w: World): void {
  const inPlay = wavesInPlay(w);
  while (w.s.wavesSpawned < inPlay) {
    const next = w.c.waves[w.s.wavesSpawned]!;
    const due = next.round <= w.s.round;
    const early = !due && w.c.rules.earlyWaveWhenEmpty && w.s.wavesSpawned > 0 && w.enemies().length === 0;
    if (!due && !early) break;
    spawnWave(w, w.s.wavesSpawned);
    if (early) break;
  }
}

export function arrivalApplies(w: World, waveIndex: number): boolean {
  const mode = w.c.rules.arrivalRound;
  return mode === 'all' || (mode === 'first' && waveIndex === 0);
}

/**
 * Spawns a wave. Units take IDs in table order. An occupied spawn tile moves the unit to the nearest
 * free tile. Every unit is placed before any mine under one goes off.
 */
export function spawnWave(w: World, index: number): Unit[] {
  const wave = w.c.waves[index]!;
  const arrival = arrivalApplies(w, index);
  const units: Unit[] = [];
  for (const entry of wave.units) {
    const tile = w.findSpawnTile(parseTile(entry.tile));
    if (!tile) continue;
    units.push(w.spawnUnit(entry.def, tile, `wave ${index + 1}`, { arrival, triggerMines: false }));
  }
  w.s.wavesSpawned = Math.max(w.s.wavesSpawned, index + 1);
  w.emit({ t: 'WaveSpawned', wave: index + 1, units: units.map((u) => u.id) });
  for (const u of units) w.triggerMineUnder(u);
  return units;
}
