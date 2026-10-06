import type { Content } from '../content/types';
import { DIR_BY_NAME } from './grid';
import { parseTile } from '../util/tiles';
import type { FightSettings, GameState, Loadout, Unit } from './types';

/** Engine version stored in every replay. Bump it when a rule change makes old command logs invalid. */
export const ENGINE_VERSION = '0.1.1';

/** The state before round 1 starts: the player on its start tile, hazards ready, nothing else. */
export function createInitialState(c: Content, loadout: Loadout, settings: FightSettings = {}): GameState {
  const pdef = c.units['player']!;
  const player: Unit = {
    id: 0,
    def: 'player',
    team: 'player',
    kind: 'player',
    pos: parseTile(c.map.playerStart),
    hp: pdef.hp,
    maxHp: pdef.hp,
    armor: pdef.armor,
    flying: false,
    elite: false,
    statuses: {},
    cooldowns: {},
    spawnRound: 0,
  };
  return {
    round: 0,
    phase: 'roundStart',
    units: [player],
    objects: [],
    hazards: c.map.hazards.map((h) => ({
      id: h.id,
      def: h.def,
      tiles: h.tiles.map(parseTile),
      dir: h.direction ? DIR_BY_NAME[h.direction] : undefined,
      state: 'ready' as const,
      cooldown: 0,
      history: [],
    })),
    nextUnitId: 1,
    nextObjectId: 1,
    nextPlacementId: 1,
    ap: 0,
    movement: 0,
    committed: false,
    wavesSpawned: 0,
    intercom: { played: [], queue: [], lineThisPhase: false, log: [] },
    loadout: { abilities: [...loadout.abilities], upgrades: [...loadout.upgrades] },
    settings: { ...settings },
    outcome: null,
    peakMinions: 0,
  };
}

export function cloneState(s: GameState): GameState {
  return structuredClone(s);
}

export function playerOf(s: GameState): Unit | undefined {
  return s.units.find((u) => u.id === 0);
}
