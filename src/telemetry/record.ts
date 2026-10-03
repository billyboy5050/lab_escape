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

/** Throws if a record was made with a different engine version or different content values. */
export function checkCompatible(c: Content, rec: FightRecord): void {
  if (rec.format !== RECORD_FORMAT) throw new ReplayMismatch(`Unknown replay format "${rec.format}"`);
  if (rec.engineVersion !== ENGINE_VERSION) throw new ReplayMismatch(`Replay was recorded on engine ${rec.engineVersion}; this is ${ENGINE_VERSION}`);
  if (rec.contentHash !== c.hash) throw new ReplayMismatch(`Replay was recorded with content ${rec.contentHash}; the current content is ${c.hash}`);
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
