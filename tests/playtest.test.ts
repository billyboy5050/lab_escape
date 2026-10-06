import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';
import { makeBot } from '../src/sim/batch';
import { analyseFight, formatPlaytest, loadTelemetry, playtestChecks, type PlaytestFight } from '../src/sim/playtest';
import { runFight } from '../src/sim/runner';
import type { FightRecord } from '../src/telemetry/record';
import { BOT_FIGHT_TIMEOUT_MS, C } from './helpers';

const fight = (o: Partial<PlaytestFight> = {}): PlaytestFight => ({
  name: 'f',
  tester: 'P1',
  attempt: 1,
  preset: 'hybrid',
  outcome: 'lose',
  rounds: 5,
  friendlyFireShare: 0.15,
  causeCategory: 'enemy',
  hazardFires: 2,
  fullSwarmTurnSeconds: [],
  ...o,
});
const check = (fights: PlaytestFight[], metric: string, opts?: Parameters<typeof playtestChecks>[1]) => playtestChecks(fights, opts).find((c) => c.metric.startsWith(metric))!;

describe('playtest metrics', () => {
  it('first attempts lost: counts testers at attempt 1 only', () => {
    const four = [1, 2, 3, 4].map((i) => fight({ tester: `P${i}` }));
    const fights = [...four, fight({ tester: 'P5', outcome: 'win' }), fight({ tester: 'P1', attempt: 2, outcome: 'win' })];
    expect(check(fights, 'First attempts lost')).toMatchObject({ value: '4 of 5 first attempts (80%)', status: 'ok' });
    expect(check(fights.slice(0, 2), 'First attempts lost').status).toBe('warn');
    expect(check([fight({ outcome: 'win' }), fight({ tester: 'P2', outcome: 'win' }), fight({ tester: 'P3' })], 'First attempts lost').status).toBe('alarm');
    expect(check([fight({ tester: undefined })], 'First attempts lost').status).toBe('n/a');
  });

  it('first-time losses: median round of lost first attempts', () => {
    expect(check([fight({ rounds: 4 }), fight({ tester: 'P2', rounds: 6 })], 'First-time losses').status).toBe('ok');
    expect(check([fight({ rounds: 3 })], 'First-time losses').status).toBe('warn');
    expect(check([fight({ rounds: 2 })], 'First-time losses').status).toBe('alarm');
  });

  it('informed win rate: pools the chosen preset from the chosen attempt, and shows each attempt', () => {
    const informed = [fight({ attempt: 2, outcome: 'win' }), fight({ tester: 'P2', attempt: 2 }), fight({ attempt: 3 }), fight({ tester: 'P2', attempt: 3, outcome: 'win' })];
    const noise = [fight({ attempt: 1, outcome: 'win' }), fight({ attempt: 2, preset: 'tech', outcome: 'win' })];
    const c = check([...informed, ...noise], 'Informed win rate');
    expect(c.value).toBe('2 of 4 (50%); attempt 2: 1/2, attempt 3: 1/2');
    expect(c.status).toBe('ok');
    expect(check([fight({ attempt: 2, outcome: 'win' })], 'Informed win rate').status).toBe('alarm');
    expect(check(informed, 'Informed win rate', { informedFrom: 3, informedPreset: 'hybrid' }).value).toContain('attempt 3: 1/2');
    expect(check([fight()], 'Informed win rate').status).toBe('n/a');
  });

  it('winning fights: median rounds', () => {
    expect(check([fight({ outcome: 'win', rounds: 10 })], 'Length of winning').status).toBe('ok');
    expect(check([fight({ outcome: 'win', rounds: 7 })], 'Length of winning').status).toBe('warn');
    expect(check([fight({ outcome: 'win', rounds: 17 })], 'Length of winning').status).toBe('alarm');
    expect(check([fight()], 'Length of winning').status).toBe('n/a');
  });

  it('turn time: median seconds over the full-swarm turns', () => {
    const t = (xs: number[]) => check([fight({ fullSwarmTurnSeconds: xs })], 'Median turn time');
    expect(t([20, 40, 100])).toMatchObject({ value: '40 s over 3 turns', status: 'ok' });
    expect(t([70]).status).toBe('warn');
    expect(t([91]).status).toBe('alarm');
    expect(t([]).status).toBe('n/a');
  });

  it('friendly fire, death sources and hazard fires', () => {
    expect(check([fight({ friendlyFireShare: 0.4 })], 'Share of player damage').status).toBe('alarm');
    expect(check([fight({ friendlyFireShare: 0.02 })], 'Share of player damage').status).toBe('alarm');
    expect(check([fight({ friendlyFireShare: 0.04 })], 'Share of player damage').status).toBe('warn');
    const hazard = [fight({ causeCategory: 'hazard' }), fight({ causeCategory: 'hazard' }), fight({ causeCategory: 'enemy' })];
    expect(check(hazard, 'Share of deaths')).toMatchObject({ value: 'hazard 67%, enemy 33%', status: 'alarm' });
    expect(check([fight({ causeCategory: 'enemy' }), fight({ causeCategory: 'enemy' }), fight({ causeCategory: 'minion' })], 'Share of deaths').status).toBe('warn');
    expect(check([fight({ hazardFires: 0 })], 'Hazard fires').status).toBe('alarm');
    expect(check([fight({ hazardFires: 1 })], 'Hazard fires').status).toBe('warn');
  });
});

describe('reading saved telemetry', () => {
  const dirs: string[] = [];
  afterEach(() => dirs.splice(0).forEach((d) => fs.rmSync(d, { recursive: true, force: true })));
  const tmp = () => {
    const d = fs.mkdtempSync(path.join(os.tmpdir(), 'lab-escape-telemetry-'));
    dirs.push(d);
    return d;
  };
  const play = (seed: number, meta: FightRecord['meta']) => {
    const hybrid = C.presets.find((p) => p.id === 'hybrid')!;
    return runFight(C, { abilities: [...hybrid.abilities], upgrades: [...hybrid.upgrades] }, makeBot('greedy', seed), { meta }).record;
  };
  const save = (root: string, name: string, rec: unknown) => {
    fs.mkdirSync(path.join(root, name));
    fs.writeFileSync(path.join(root, name, 'replay.json'), JSON.stringify(rec));
  };

  it('pairs each turn time with the swarm at the end of that turn, and counts only full swarms', { timeout: BOT_FIGHT_TIMEOUT_MS }, () => {
    const rec = play(1, { preset: 'hybrid', tester: 'P1', attempt: 1 });
    const turns = rec.commands.filter((c) => c.type === 'endTurn').length + (rec.commands.at(-1)!.type === 'endTurn' ? 0 : 1);
    // One time per turn, 1 second for the first turn, then 2, 3, ...
    rec.meta!.turnTimesMs = Array.from({ length: turns }, (_, i) => (i + 1) * 1000);
    const f = analyseFight(C, rec, 'x');
    expect(typeof f).not.toBe('string');
    if (typeof f === 'string') return;
    expect(f.tester).toBe('P1');
    expect(f.attempt).toBe(1);
    expect(f.fullSwarmTurnSeconds.length).toBeLessThanOrEqual(turns);
    for (const s of f.fullSwarmTurnSeconds) expect(s).toBeGreaterThanOrEqual(1);
    // With no turn times recorded there is nothing to measure.
    const bare = analyseFight(C, { ...rec, meta: { preset: 'hybrid' } }, 'y');
    expect(typeof bare === 'string' ? bare : bare.fullSwarmTurnSeconds).toEqual([]);
  });

  it('reads good fights and reports every folder it skips, with the reason', { timeout: BOT_FIGHT_TIMEOUT_MS }, () => {
    const root = tmp();
    save(root, 'a-good', play(1, { preset: 'hybrid', tester: 'P1', attempt: 1 }));
    save(root, 'b-other-engine', { ...play(2, {}), engineVersion: '0.0.1' });
    save(root, 'c-cheat', { ...play(3, {}), commands: [{ type: 'debug', op: 'forceWave' }] });
    save(root, 'd-junk', { nope: true });
    fs.mkdirSync(path.join(root, 'e-empty'));
    fs.writeFileSync(path.join(root, 'loose-file.txt'), 'ignored');
    const load = loadTelemetry(C, root);
    expect(load.fights.map((f) => f.name)).toEqual(['a-good']);
    expect(Object.fromEntries(load.skipped.map((s) => [s.name, s.reason]))).toMatchObject({
      'b-other-engine': expect.stringContaining('engine'),
      'c-cheat': 'used debug commands',
      'd-junk': expect.any(String),
      'e-empty': 'no replay.json',
    });
    const text = formatPlaytest(root, load);
    expect(text).toContain('Fights counted: 1; skipped: 4');
    expect(text).toContain('P1: a1 hybrid');
    expect(text).toContain('[');
  });

  it('says what is wrong when the folder does not exist', () => {
    expect(() => loadTelemetry(C, path.join(os.tmpdir(), 'lab-escape-no-such-folder'))).toThrow(/No telemetry folder/);
  });
});
