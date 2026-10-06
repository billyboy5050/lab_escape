import { describe, expect, it } from 'vitest';
import { legalCommands } from '../src/engine/commands';
import { newGame, step } from '../src/engine/step';
import { computeIntents, previewCommand } from '../src/preview/preview';
import { GreedyBot, RandomBot } from '../src/sim/bots';
import { Rng } from '../src/sim/rng';
import { runFight } from '../src/sim/runner';
import type { GameState } from '../src/state/types';
import { checkCompatible, replay, ReplayMismatch } from '../src/telemetry/record';
import { FightSession } from '../src/telemetry/session';
import { BOT_FIGHT_TIMEOUT_MS, C, contentWith } from './helpers';

const loadoutOf = (id: string) => {
  const p = C.presets.find((x) => x.id === id)!;
  return { abilities: p.abilities, upgrades: p.upgrades };
};

/** States drawn by playing a random bot for a random number of commands. */
function randomStates(count: number, seed: number): GameState[] {
  const rng = new Rng(seed);
  const out: GameState[] = [];
  const presets = C.presets.map((p) => p.id);
  while (out.length < count) {
    let s = newGame(C, loadoutOf(presets[rng.int(presets.length)]!)).state;
    const bot = new RandomBot(rng.nextU32());
    const steps = rng.int(40);
    for (let i = 0; i < steps && !s.outcome; i++) s = step(C, s, bot.decide(C, s)).state;
    if (!s.outcome) out.push(s);
  }
  return out;
}

describe('previews and determinism', () => {
  const states = randomStates(100, 7);

  it('the dry-run preview matches the real result for 100 random states and commands', () => {
    const rng = new Rng(99);
    for (const s of states) {
      const before = JSON.stringify(s);
      const cmds = legalCommands(C, s);
      const cmd = cmds[rng.int(cmds.length)]!;
      const preview = previewCommand(C, s, cmd);
      const real = step(C, s, cmd);
      expect(preview.ok).toBe(true);
      expect(preview.events).toEqual(real.events);
      expect(preview.state).toEqual(real.state);
      // Neither the preview nor the step touched the input state.
      expect(JSON.stringify(s)).toBe(before);
    }
  });

  it('intents show exactly what the minion and enemy phases do when the turn ends', () => {
    for (const s of states.slice(0, 50)) {
      const rep = computeIntents(C, s);
      const real = step(C, s, { type: 'endTurn' });
      const envAt = real.events.findIndex((e) => e.t === 'PhaseStarted' && e.phase === 'environment');
      const actorEvents = envAt >= 0 ? real.events.slice(0, envAt) : real.events.filter((e) => e.t !== 'FightEnded');
      const simEvents = rep.frames.flatMap((f) => f.events).filter((e) => e.t !== 'FightEnded');
      expect(simEvents).toEqual(actorEvents.filter((e) => e.t !== 'FightEnded'));
    }
  });

  it('replaying a command log reproduces the fight exactly', () => {
    for (const [i, id] of ['tech', 'alien', 'hybrid'].entries()) {
      const r = runFight(C, loadoutOf(id), new GreedyBot(100 + i));
      const rep = replay(C, r.record);
      expect(rep.state).toEqual(r.state);
    }
  }, BOT_FIGHT_TIMEOUT_MS);

  it('a replay is only valid against the same engine version and content hash', () => {
    const r = runFight(C, loadoutOf('tech'), new RandomBot(3));
    const tuned = contentWith({ 'units.warden.hp': 16 });
    expect(tuned.hash).not.toBe(C.hash);
    expect(() => checkCompatible(tuned, r.record)).toThrow(ReplayMismatch);
    expect(() => replay(tuned, r.record)).toThrow(ReplayMismatch);
  });

  it('the same bot and seed always plays the same fight', () => {
    const a = runFight(C, loadoutOf('hybrid'), new GreedyBot(5));
    const b = runFight(C, loadoutOf('hybrid'), new GreedyBot(5));
    expect(a.record.commands).toEqual(b.record.commands);
    expect(a.summary).toEqual(b.summary);
  }, BOT_FIGHT_TIMEOUT_MS);
});

describe('undo for free movement', () => {
  it('free moves can be undone, several levels deep, until AP is spent', () => {
    const f = new FightSession(C, loadoutOf('tech'), {}, {}, () => 0);
    const start = f.state;
    expect(f.apply({ type: 'move', path: ['B7'] }).ok).toBe(true);
    expect(f.apply({ type: 'move', path: ['C7'] }).ok).toBe(true);
    expect(f.canUndo).toBe(true);
    f.undo();
    f.undo();
    expect(f.state).toEqual(start);
    expect(f.record.commands).toEqual([]);
    f.apply({ type: 'move', path: ['B7'] });
    f.apply({ type: 'sprint' });
    expect(f.canUndo).toBe(false);
  });

  it('a move that sets something off commits the turn', () => {
    const f = new FightSession(C, loadoutOf('tech'), {}, {}, () => 0);
    f.apply({ type: 'ability', ability: 'proximity_mine', target: 'B6' });
    expect(f.canUndo).toBe(false);
    const r = f.apply({ type: 'move', path: ['B7', 'B6'] });
    expect(r.commits).toBe(true);
    expect(f.canUndo).toBe(false);
  });

  it('the command log of a session replays to the same state', () => {
    const f = new FightSession(C, loadoutOf('alien'), {}, {}, () => 0);
    const bot = new GreedyBot(11);
    for (let i = 0; i < 30 && !f.over; i++) f.apply(bot.decide(C, f.state));
    expect(replay(C, f.record).state).toEqual(f.state);
  });
});
