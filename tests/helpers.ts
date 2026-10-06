import { buildContent, defaultContent, defaultContentFiles, withOverrides, type Content } from '../src/content';
import { createWorld } from '../src/engine/phases';
import { step, type StepResult } from '../src/engine/step';
import { FightOver, type World } from '../src/rules/world';
import { createInitialState } from '../src/state/state';
import type { Command, GameEvent, GameObject, GameState, Loadout, Statuses, Unit } from '../src/state/types';
import { parseTile, tileName } from '../src/util/tiles';

export const C = defaultContent();

/**
 * Timeout for tests that play whole bot fights. They take a second or two on a developer machine and several times that
 * on a shared CI runner, past Vitest's 5 s default.
 */
export const BOT_FIGHT_TIMEOUT_MS = 30_000;

export function contentWith(overrides: Record<string, unknown>): Content {
  return buildContent(withOverrides(defaultContentFiles(), overrides));
}

export const ALL_LOADOUT: Loadout = {
  abilities: ['sidearm', 'grapple_hook', 'proximity_mine', 'auto_turret', 'scout_drone', 'barrier_shield', 'acid_spit', 'lunge', 'spore_pod', 'brood_egg', 'parasite'],
  upgrades: [],
};

export interface UnitSpec {
  def: string;
  at: string;
  hp?: number;
  statuses?: Statuses;
  arrival?: boolean;
  ammo?: number;
  charges?: number;
  droneState?: 'deployed' | 'returning' | 'docked';
  armor?: number;
}

export interface ObjectSpec {
  kind: GameObject['kind'];
  at: string;
  hp?: number;
  timer?: number;
  host?: string;
  hatchInto?: string;
  of?: string;
  decay?: number;
  placement?: number;
}

export interface ScenarioSpec {
  player?: string;
  playerHp?: number;
  units?: UnitSpec[];
  objects?: ObjectSpec[];
  upgrades?: string[];
  abilities?: string[];
  round?: number;
  ap?: number;
  movement?: number;
  /** Waves already spawned. The default (2 of 3) means no wave spawns before round 7 and nobody wins early. */
  wavesSpawned?: number;
  content?: Content;
}

/**
 * Builds a state in the player phase with exactly the given units and objects. Units take IDs in the
 * order listed, after the player (ID 0).
 */
export function scenario(spec: ScenarioSpec = {}): GameState {
  const c = spec.content ?? C;
  const s = createInitialState(c, { abilities: spec.abilities ?? ALL_LOADOUT.abilities, upgrades: spec.upgrades ?? [] });
  s.round = spec.round ?? 2;
  s.phase = 'player';
  s.ap = spec.ap ?? c.rules.turn.ap;
  s.movement = spec.movement ?? c.rules.turn.movement;
  s.wavesSpawned = spec.wavesSpawned ?? 2;
  const p = s.units[0]!;
  if (spec.player) p.pos = parseTile(spec.player);
  if (spec.playerHp !== undefined) p.hp = spec.playerHp;
  for (const u of spec.units ?? []) s.units.push(makeUnit(c, s, u));
  for (const o of spec.objects ?? []) {
    const obj: GameObject = { id: s.nextObjectId++, kind: o.kind, pos: parseTile(o.at), createdRound: s.round };
    if (o.kind === 'mine') obj.team = 'player';
    if (o.kind === 'shield') Object.assign(obj, { team: 'player', hp: o.hp ?? 4, maxHp: 4, placement: o.placement ?? s.nextPlacementId++ });
    if (o.kind === 'egg') Object.assign(obj, { team: 'player', hp: o.hp ?? c.rules.eggs.hp, maxHp: c.rules.eggs.hp, timer: o.timer ?? 1, host: o.host ?? 'floor', hatchInto: o.hatchInto ?? 'hatchling' });
    if (o.kind === 'corpse') Object.assign(obj, { team: 'enemy', of: o.of ?? 'guard', decay: o.decay ?? c.rules.corpseDecay });
    s.objects.push(obj);
  }
  return s;
}

export function makeUnit(c: Content, s: GameState, spec: UnitSpec): Unit {
  const def = c.units[spec.def]!;
  const u: Unit = {
    id: s.nextUnitId++,
    def: spec.def,
    team: def.team,
    kind: def.kind,
    pos: parseTile(spec.at),
    hp: spec.hp ?? def.hp,
    maxHp: def.hp,
    armor: spec.armor ?? def.armor,
    flying: !!def.flying,
    elite: !!def.elite,
    statuses: spec.statuses ?? {},
    cooldowns: def.purge ? { purge: 0 } : {},
    spawnRound: s.round,
  };
  if (def.ammo !== undefined) {
    u.maxAmmo = def.ammo;
    u.ammo = spec.ammo ?? def.ammo;
  }
  if (def.charges !== undefined) {
    u.maxCharges = def.charges;
    u.charges = spec.charges ?? def.charges;
    u.droneState = spec.droneState ?? 'deployed';
    u.returningPhases = 0;
  }
  if (spec.arrival) u.arrivalRound = s.round;
  return u;
}

/** Runs a step and fails loudly if the command was illegal. */
export function run(s: GameState, cmd: Command, c: Content = C): StepResult {
  const r = step(c, s, cmd);
  if (!r.ok) throw new Error(`Command rejected: ${r.error}`);
  return r;
}

/** Runs a function inside a root action on a copy of the state, for testing rules directly. */
export function inWorld(s: GameState, fn: (w: World) => void, c: Content = C): { w: World; s: GameState; events: GameEvent[] } {
  const copy = structuredClone(s);
  const w = createWorld(c, copy);
  try {
    w.root('test', null, () => fn(w));
  } catch (e) {
    if (!(e instanceof FightOver)) throw e;
  }
  return { w, s: copy, events: w.events };
}

export const T = parseTile;
export const name = tileName;

export function unitAt(s: GameState, tile: string): Unit | undefined {
  const p = parseTile(tile);
  return s.units.find((u) => u.pos.x === p.x && u.pos.y === p.y && u.droneState !== 'docked');
}

export function unitById(s: GameState, id: number): Unit | undefined {
  return s.units.find((u) => u.id === id);
}

export function damageTo(events: GameEvent[], id: number): number {
  return events.filter((e) => e.t === 'DamageDealt' && e.target.kind === 'unit' && e.target.id === id).reduce((a, e) => a + (e as { amount: number }).amount, 0);
}

export function eventsOf<K extends GameEvent['t']>(events: GameEvent[], t: K): Extract<GameEvent, { t: K }>[] {
  return events.filter((e) => e.t === t) as Extract<GameEvent, { t: K }>[];
}
