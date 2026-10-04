import type { Content } from '../content/types';
import { newGame, step, type StepResult } from '../engine/step';
import type { Command, FightSettings, Frame, GameEvent, GameState, Loadout } from '../state/types';
import { newRecord, type FightRecord } from './record';
import { summarize, type FightSummary } from './summary';

interface UndoEntry {
  state: GameState;
  commands: number;
  events: number;
}

/**
 * One fight as the client plays it: the current state, the command log for replay, every event so far,
 * and undo for free movement. Undo works until the player spends AP or a move sets something off;
 * undone moves are dropped from the log, so the log replays exactly what happened.
 */
export class FightSession {
  state: GameState;
  readonly record: FightRecord;
  readonly events: GameEvent[];
  readonly startFrames: Frame[];
  private undoStack: UndoEntry[] = [];
  private turnStart: number;
  /** True from the start of a player turn until turnReady() restarts the clock, once the client has finished animating. */
  private clockPending = true;

  constructor(
    readonly c: Content,
    loadout: Loadout,
    settings: FightSettings = {},
    meta: FightRecord['meta'] = {},
    private readonly now: () => number = () => Date.now(),
  ) {
    const g = newGame(c, loadout, settings, { frames: true });
    this.state = g.state;
    this.events = [...g.events];
    this.startFrames = g.frames;
    this.record = newRecord(c, loadout, settings, { created: new Date(this.now()).toISOString(), turnTimesMs: [], ...meta });
    this.turnStart = this.now();
  }

  /** Applies a command. Illegal commands leave everything unchanged and return ok: false. */
  apply(cmd: Command, opts: { frames?: boolean } = { frames: true }): StepResult {
    const before = this.state;
    const r = step(this.c, before, cmd, opts);
    if (!r.ok) return r;
    if (cmd.type === 'move' && !r.commits) {
      this.undoStack.push({ state: before, commands: this.record.commands.length, events: this.events.length });
    } else {
      this.undoStack = [];
    }
    // A turn ends when the player ends it, or when the fight does: the winning (or fatal) action closes the last turn.
    if (cmd.type === 'endTurn' || r.state.outcome) {
      this.record.meta?.turnTimesMs?.push(Math.max(0, Math.round(this.now() - this.turnStart)));
    }
    if (cmd.type === 'endTurn') {
      // Timed from here unless the client calls turnReady(), which is how it leaves out the enemy phase playing out.
      this.turnStart = this.now();
      this.clockPending = true;
    }
    this.record.commands.push(cmd);
    this.events.push(...r.events);
    this.state = r.state;
    return r;
  }

  /**
   * The client calls this once the animation has finished and the player can act. It starts the clock for the
   * turn that has just begun (the first turn, or the one after an End Turn), so a turn time measures the player's
   * own time and not the opening or the enemy and environment phases playing out. Later calls in the same turn do
   * nothing. Callers that never call it (the simulator, tests) time a turn from the end of the previous one.
   */
  turnReady(): void {
    if (!this.clockPending || this.state.outcome || this.state.phase !== 'player') return;
    this.turnStart = this.now();
    this.clockPending = false;
  }

  get canUndo(): boolean {
    return this.undoStack.length > 0 && !this.state.outcome;
  }

  /** Undoes the last free move. Returns false when there is nothing to undo. */
  undo(): boolean {
    const u = this.undoStack.pop();
    if (!u) return false;
    this.state = u.state;
    this.record.commands.length = u.commands;
    this.events.length = u.events;
    return true;
  }

  get over(): boolean {
    return !!this.state.outcome;
  }

  summary(): FightSummary {
    return summarize(this.events, this.state);
  }
}
