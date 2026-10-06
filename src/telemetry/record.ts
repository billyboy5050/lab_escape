import type { Content } from '../content/types';
import { newGame, step } from '../engine/step';
import { ENGINE_VERSION } from '../state/state';
import type { Command, FightSettings, Frame, GameEvent, GameState, Loadout } from '../state/types';

export const RECORD_FORMAT = 'lab-escape/replay@1';

/**
 * A fight is fully described by its loadout, settings and the ordered player commands. Replaying them
 * against the same engine version and content hash reproduces the fight exactly.
 */
export interface FightRecord {
  format: typeof RECORD_FORMAT;
  engineVersion: string;
  contentHash: string;
  loadout: Loadout;
  settings: FightSettings;
  commands: Command[];
  meta?: {
    created?: string;
    label?: string;
    bot?: string;
    seed?: number;
    preset?: string;
    /** Who played it, from the playtest: a short ID such as P3. Telemetry only; replay ignores it. */
    tester?: string;
    /** This tester's attempt number, counting from 1. Telemetry only; replay ignores it. */
    attempt?: number;
    /** Wall-clock milliseconds the player spent on each turn. Telemetry only; replay ignores it. */
    turnTimesMs?: number[];
  };
}

export function newRecord(c: Content, loadout: Loadout, settings: FightSettings = {}, meta?: FightRecord['meta']): FightRecord {
  return { format: RECORD_FORMAT, engineVersion: ENGINE_VERSION, contentHash: c.hash, loadout, settings, commands: [], meta };
}

export class ReplayMismatch extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ReplayMismatch';
  }
}

const COMMAND_TYPES: readonly string[] = ['move', 'sprint', 'ability', 'reload', 'pickUpMine', 'redeploy', 'endTurn', 'debug'];
const DEBUG_OPS: readonly string[] = ['setHp', 'setAp', 'spawn', 'forceWave'];
/** How many problems a rejection message lists; a file with thousands of bad commands would otherwise print them all. */
const MAX_PROBLEMS = 6;

const isObject = (v: unknown): v is Record<string, unknown> => typeof v === 'object' && v !== null && !Array.isArray(v);

type Kind = 'text' | 'number' | 'whole' | 'tiles' | 'mode';
const KIND: Record<Kind, { ok: (v: unknown) => boolean; noun: string }> = {
  text: { ok: (v) => typeof v === 'string', noun: 'text' },
  number: { ok: (v) => typeof v === 'number' && Number.isFinite(v), noun: 'a number' },
  whole: { ok: (v) => typeof v === 'number' && Number.isInteger(v), noun: 'a whole number' },
  tiles: { ok: (v) => Array.isArray(v) && v.every((x) => typeof x === 'string'), noun: 'a list of tile names' },
  mode: { ok: (v) => v === 'self' || v === 'unit', noun: '"self" or "unit"' },
};
/** The fields each command carries, by type: those it needs, and those it may have (null counts as absent). */
type Fields = { need: Record<string, Kind>; may?: Record<string, Kind> };
const COMMAND_FIELDS: Record<string, Fields> = {
  move: { need: { path: 'tiles' } },
  sprint: { need: {} },
  ability: { need: { ability: 'text', target: 'text' }, may: { target2: 'text', mode: 'mode' } },
  reload: { need: { target: 'text' } },
  pickUpMine: { need: { target: 'text' } },
  redeploy: { need: { drone: 'whole', target: 'text' } },
  endTurn: { need: {} },
};
const DEBUG_FIELDS: Record<string, Fields> = {
  setHp: { need: { unit: 'whole', value: 'number' } },
  setAp: { need: { ap: 'number' }, may: { movement: 'number' } },
  spawn: { need: { def: 'text', target: 'text' } },
  forceWave: { need: {} },
};

/**
 * Structural problems in parsed replay JSON: the parts of a record that the loaders and screens read without checking
 * (loadout, settings, the command list, the turn times) must exist and have the right types, and so must the fields of
 * each command. Whether a command is legal is the engine's business: it rejects an illegal one with a message, and
 * replay() reports it. Stops after a few problems.
 */
export function recordProblems(rec: unknown): string[] {
  const p: string[] = [];
  const add = (msg: string) => {
    if (p.length <= MAX_PROBLEMS) p.push(msg);
  };
  if (!isObject(rec)) return ['the file is not a replay file'];
  const names = (v: unknown, where: string) => {
    if (!Array.isArray(v)) return add(`${where} must be a list`);
    v.forEach((x, i) => typeof x !== 'string' && add(`${where}[${i}] must be a name (got ${JSON.stringify(x)})`));
  };
  if (!isObject(rec.loadout)) add('loadout must be an object with abilities and upgrades');
  else {
    names(rec.loadout.abilities, 'loadout.abilities');
    names(rec.loadout.upgrades, 'loadout.upgrades');
  }
  if (!isObject(rec.settings)) add('settings must be an object');
  else {
    const w = rec.settings.maxWaves;
    if (w !== undefined && (typeof w !== 'number' || !Number.isInteger(w) || w < 1)) add(`settings.maxWaves must be a whole number of at least 1 (got ${JSON.stringify(w)})`);
  }
  if (!Array.isArray(rec.commands)) add('commands must be a list');
  else {
    rec.commands.forEach((cmd, i) => {
      if (!isObject(cmd) || typeof cmd.type !== 'string') add(`commands[${i}] must be an object with a type`);
      else if (!COMMAND_TYPES.includes(cmd.type)) add(`commands[${i}] has unknown type ${JSON.stringify(cmd.type)}`);
      else if (cmd.type === 'debug' && !DEBUG_OPS.includes(cmd.op as string)) add(`commands[${i}] has unknown debug op ${JSON.stringify(cmd.op)}`);
      else {
        // The engine turns a command it will not allow into a message, but it does arithmetic on a bad number
        // (an AP of "oops" becomes NaN and nothing is refused after that), so field types are checked here.
        const fields = cmd.type === 'debug' ? DEBUG_FIELDS[cmd.op as string]! : COMMAND_FIELDS[cmd.type]!;
        for (const [key, kind] of Object.entries(fields.need)) {
          if (!KIND[kind].ok(cmd[key])) add(`commands[${i}].${key} must be ${KIND[kind].noun} (got ${JSON.stringify(cmd[key])})`);
        }
        for (const [key, kind] of Object.entries(fields.may ?? {})) {
          if (cmd[key] !== undefined && cmd[key] !== null && !KIND[kind].ok(cmd[key])) add(`commands[${i}].${key} must be ${KIND[kind].noun} (got ${JSON.stringify(cmd[key])})`);
        }
      }
    });
  }
  const meta = rec.meta;
  if (meta !== undefined) {
    if (!isObject(meta)) add('meta must be an object');
    else {
      for (const k of ['created', 'label', 'bot', 'preset', 'tester'] as const) if (meta[k] !== undefined && typeof meta[k] !== 'string') add(`meta.${k} must be text`);
      if (meta.seed !== undefined && typeof meta.seed !== 'number') add('meta.seed must be a number');
      if (meta.attempt !== undefined && !(typeof meta.attempt === 'number' && Number.isInteger(meta.attempt) && meta.attempt >= 1)) add('meta.attempt must be a whole number of at least 1');
      const t = meta.turnTimesMs;
      if (t !== undefined) {
        if (!Array.isArray(t)) add('meta.turnTimesMs must be a list of numbers');
        else t.forEach((x, i) => (typeof x !== 'number' || !Number.isFinite(x) || x < 0) && add(`meta.turnTimesMs[${i}] must be a number of milliseconds, 0 or more (got ${JSON.stringify(x)})`));
      }
    }
  }
  return p;
}

/**
 * Throws a ReplayMismatch unless the parsed JSON is a replay of this engine version and content that has the whole
 * record shape. Callers parse untrusted files, so the argument is unknown and a pass narrows it to a FightRecord.
 * The header is checked first: an old file should be told it is old, not that its shape is wrong.
 */
export function checkCompatible(c: Content, rec: unknown): asserts rec is FightRecord {
  if (!isObject(rec)) throw new ReplayMismatch('The file is not a replay file');
  if (rec.format !== RECORD_FORMAT) throw new ReplayMismatch(`Unknown replay format "${String(rec.format)}"`);
  if (rec.engineVersion !== ENGINE_VERSION) throw new ReplayMismatch(`Replay was recorded on engine ${String(rec.engineVersion)}; this is ${ENGINE_VERSION}`);
  if (rec.contentHash !== c.hash) throw new ReplayMismatch(`Replay was recorded with content ${String(rec.contentHash)}; the current content is ${c.hash}`);
  const problems = recordProblems(rec);
  if (problems.length) {
    const shown = problems.slice(0, MAX_PROBLEMS);
    const more = problems.length > MAX_PROBLEMS ? ['(further problems not shown)'] : [];
    throw new ReplayMismatch(`The replay file is malformed:\n  - ${[...shown, ...more].join('\n  - ')}`);
  }
}

export interface ReplayResult {
  state: GameState;
  events: GameEvent[];
  frames: Frame[];
  /** State after each command (index 0 is the start of round 1). */
  states: GameState[];
}

export function replay(c: Content, rec: FightRecord, opts: { frames?: boolean; strict?: boolean; keepStates?: boolean } = {}): ReplayResult {
  if (opts.strict !== false) checkCompatible(c, rec);
  const g = newGame(c, rec.loadout, rec.settings, { frames: opts.frames });
  let state = g.state;
  const events = [...g.events];
  const frames = [...g.frames];
  const states = opts.keepStates ? [state] : [];
  rec.commands.forEach((cmd, i) => {
    const r = step(c, state, cmd, { frames: opts.frames });
    if (!r.ok) throw new ReplayMismatch(`Command ${i + 1} (${cmd.type}) was rejected: ${r.error}`);
    state = r.state;
    events.push(...r.events);
    frames.push(...r.frames);
    if (opts.keepStates) states.push(state);
  });
  return { state, events, frames, states };
}

/** The event stream as JSON lines, one event per line. */
export function eventsToJsonl(events: GameEvent[]): string {
  return events.map((e) => JSON.stringify(e)).join('\n') + '\n';
}
