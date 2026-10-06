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
  playerDamage: 6,
  causeCategory: 'enemy',
  hazardFires: 2,
  fullSwarmTurnSeconds: [],
  turnTimesComplete: true,
  ...o,
});
const check = (fights: PlaytestFight[], metric: string, opts?: Parameters<typeof playtestChecks>[1]) => playtestChecks(fights, opts).find((c) => c.metric.startsWith(metric))!;

describe('playtest metrics', () => {
  it('first attempts lost: counts testers at attempt 1 only', () => {
    const four = [1, 2, 3, 4].map((i) => fight({ tester: `P${i}` }));
    const fights = [...four, fight({ tester: 'P5', outcome: 'win' }), fight({ tester: 'P1', attempt: 2, outcome: 'win' })];
    expect(check(fights, 'First attempts lost')).toMatchObject({ value: '4 of 5 testers (80.0%); 3 with no fight after the loss', status: 'ok' });
    expect(check(fights.slice(0, 2), 'First attempts lost').status).toBe('warn');
    expect(check([fight({ outcome: 'win' }), fight({ tester: 'P2', outcome: 'win' }), fight({ tester: 'P3' })], 'First attempts lost').status).toBe('alarm');
    expect(check([fight({ tester: undefined })], 'First attempts lost').status).toBe('n/a');
  });

  it('first attempts: one per tester (the earliest), and how many testers have no fight after the loss', () => {
    const fights = [fight({ name: '1' }), fight({ name: '2', outcome: 'win' }), fight({ tester: 'P2', name: '3' }), fight({ tester: 'P2', name: '4', attempt: 2, outcome: 'win' })];
    expect(check(fights, 'First attempts lost').value).toBe('2 of 2 testers (100.0%); 1 with no fight after the loss');
  });

  it('informed round: only the attempts it has, from the first informed one', () => {
    const fights = [2, 3, 4, 5, 6].map((a) => fight({ attempt: a, outcome: 'win' }));
    expect(check(fights, 'Informed win rate').value).toBe('3 of 3 (100.0%); attempt 2: 1/1, attempt 3: 1/1, attempt 4: 1/1');
    expect(check(fights, 'Informed win rate', { informedFrom: 2, informedAttempts: 2, informedPreset: 'hybrid' }).value).toContain('2 of 2');
  });

  it('first-time losses: every lost attempt before the informed round, one per tester and attempt number', () => {
    const fights = [fight({ rounds: 5 }), fight({ attempt: 2, rounds: 2 }), fight({ attempt: 2, rounds: 9, name: 'dup' }), fight({ attempt: 3, rounds: 1 })];
    expect(check(fights, 'First-time losses', { informedFrom: 3, informedAttempts: 3, informedPreset: 'hybrid' }).value).toBe('median loss in round 3.5');
    expect(check(fights, 'First-time losses').value).toBe('median loss in round 5');
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
    expect(c.value).toBe('2 of 4 (50.0%); attempt 2: 1/2, attempt 3: 1/2');
    expect(c.status).toBe('ok');
    expect(check([fight({ attempt: 2, outcome: 'win' })], 'Informed win rate').status).toBe('alarm');
    expect(check(informed, 'Informed win rate', { informedFrom: 3, informedAttempts: 3, informedPreset: 'hybrid' }).value).toContain('attempt 3: 1/2');
    expect(check([fight()], 'Informed win rate').status).toBe('n/a');
  });

  it('informed round: one fight per tester and attempt number, the earliest', () => {
    const fights = [fight({ name: '1', attempt: 2, outcome: 'win' }), fight({ name: '2', attempt: 2 }), fight({ name: '3', attempt: 3 })];
    expect(check(fights, 'Informed win rate').value).toBe('1 of 2 (50.0%); attempt 2: 1/1, attempt 3: 0/1');
  });

  it('winning fights: median rounds', () => {
    expect(check([fight({ outcome: 'win', rounds: 10 })], 'Length of winning').status).toBe('ok');
    expect(check([fight({ outcome: 'win', rounds: 7 })], 'Length of winning').status).toBe('warn');
    expect(check([fight({ outcome: 'win', rounds: 17 })], 'Length of winning').status).toBe('alarm');
    expect(check([fight()], 'Length of winning').status).toBe('n/a');
  });

  it('turn time: median seconds over the full-swarm turns', () => {
    const t = (xs: number[]) => check([fight({ fullSwarmTurnSeconds: xs })], 'Median turn time');
    expect(t([20, 40, 100])).toMatchObject({ value: '40.0 s over 3 turns', status: 'ok' });
    // The figure shown is the figure judged: 59.96 s shows as 60.0 s and is not "under 60 s".
    expect(t([59.96])).toMatchObject({ value: '60.0 s over 1 turns', status: 'warn' });
    expect(t([90.04])).toMatchObject({ value: '90.0 s over 1 turns', status: 'warn' });
    expect(t([70]).status).toBe('warn');
    expect(t([91]).status).toBe('alarm');
    expect(t([]).status).toBe('n/a');
    const note = check([fight({ fullSwarmTurnSeconds: [30] }), fight({ turnTimesComplete: false })], 'Median turn time');
    expect(note.value).toBe('30.0 s over 1 turns; 1 of 2 fights left out for missing or incomplete turn times');
  });

  it('friendly fire, death sources and hazard fires', () => {
    expect(check([fight({ friendlyFireShare: 0.4 })], 'Share of player damage').status).toBe('alarm');
    expect(check([fight({ friendlyFireShare: 0.02 })], 'Share of player damage').status).toBe('alarm');
    expect(check([fight({ friendlyFireShare: 0.04 })], 'Share of player damage').status).toBe('warn');
    // Fights where the player took no damage have no share, and are left out of the mean.
    const flawless = Array.from({ length: 9 }, () => fight({ friendlyFireShare: 0, playerDamage: 0 }));
    expect(check([fight({ friendlyFireShare: 0.1 }), ...flawless], 'Share of player damage')).toMatchObject({ value: '10.0% (1 fights where the player took damage)', status: 'ok' });
    expect(check(flawless, 'Share of player damage').status).toBe('n/a');
    expect(check([fight({ friendlyFireShare: 0.352 })], 'Share of player damage')).toMatchObject({ value: expect.stringContaining('35.2%'), status: 'alarm' });
    const hazard = [fight({ causeCategory: 'hazard' }), fight({ causeCategory: 'hazard' }), fight({ causeCategory: 'enemy' })];
    expect(check(hazard, 'Share of deaths')).toMatchObject({ value: 'hazard 66.7%, enemy 33.3%', status: 'alarm' });
    expect(check([fight({ causeCategory: 'enemy' }), fight({ causeCategory: 'enemy' }), fight({ causeCategory: 'minion' })], 'Share of deaths').status).toBe('warn');
    expect(check([fight({ causeCategory: null }), fight({ causeCategory: 'enemy' })], 'Share of deaths').value).toBe('enemy 50.0%, round cap 50.0%');
    expect(check([fight({ hazardFires: 0 })], 'Hazard fires').status).toBe('alarm');
    expect(check([fight({ hazardFires: 1 })], 'Hazard fires').status).toBe('warn');
    expect(check([fight({ outcome: 'win' })], 'Share of deaths').status).toBe('n/a');
    expect(check([], 'Hazard fires').status).toBe('n/a');
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
    // A missing time shifts the rest onto the wrong turns, so the fight gives no turn time at all.
    const short = analyseFight(C, { ...rec, meta: { ...rec.meta, turnTimesMs: rec.meta!.turnTimesMs!.slice(1) } }, 'z');
    expect(typeof short === 'string' ? short : [short.turnTimesComplete, short.fullSwarmTurnSeconds]).toEqual([false, []]);
    expect(f.turnTimesComplete).toBe(true);
    // With no turn times recorded there is nothing to measure.
    const bare = analyseFight(C, { ...rec, meta: { preset: 'hybrid' } }, 'y');
    expect(typeof bare === 'string' ? bare : bare.fullSwarmTurnSeconds).toEqual([]);
  });

  it('reads good fights and reports every folder it skips, with the reason', { timeout: BOT_FIGHT_TIMEOUT_MS }, () => {
    const root = tmp();
    save(root, 'a-good', play(1, { preset: 'hybrid', tester: 'P1', attempt: 1 }));
    save(root, 'b-other-engine', { ...play(2, {}), engineVersion: '0.0.1' });
    save(root, 'c-cheat', { ...play(3, {}), commands: [{ type: 'debug', op: 'forceWave' }] });
    save(root, 'f-held-back', { ...play(4, {}), settings: { maxWaves: 2 } });
    save(root, 'd-junk', { nope: true });
    fs.mkdirSync(path.join(root, 'e-empty'));
    fs.writeFileSync(path.join(root, 'loose-file.txt'), 'ignored');
    const load = loadTelemetry(C, root);
    expect(load.fights.map((f) => f.name)).toEqual(['a-good']);
    expect(Object.fromEntries(load.skipped.map((s) => [s.name, s.reason]))).toMatchObject({
      'b-other-engine': expect.stringContaining('engine'),
      'c-cheat': 'used debug commands',
      'f-held-back': 'played with wave 3 held back',
      'd-junk': expect.any(String),
      'e-empty': 'no replay.json',
    });
    const text = formatPlaytest(root, load);
    expect(text).toContain('Fights counted: 1; skipped: 5');
    expect(text).toContain('P1: a1 hybrid');
    expect(text).toContain('[');
  });

  it('skips fights with no tester ID unless asked to count them', { timeout: BOT_FIGHT_TIMEOUT_MS }, () => {
    const root = tmp();
    save(root, 'a-labelled', play(1, { preset: 'hybrid', tester: 'P1', attempt: 1 }));
    save(root, 'b-ordinary-play', play(2, { preset: 'hybrid' }));
    const load = loadTelemetry(C, root);
    expect(load.fights.map((f) => f.name)).toEqual(['a-labelled']);
    expect(load.skipped).toEqual([{ name: 'b-ordinary-play', reason: expect.stringContaining('no tester ID') }]);
    expect(loadTelemetry(C, root, { includeUnlabelled: true }).fights.map((f) => f.name)).toEqual(['a-labelled', 'b-ordinary-play']);
  });

  it('refuses a replay with a negative turn time', { timeout: BOT_FIGHT_TIMEOUT_MS }, () => {
    const root = tmp();
    save(root, 'a-negative', play(1, { preset: 'hybrid', tester: 'P1', attempt: 1, turnTimesMs: [-5000] }));
    const load = loadTelemetry(C, root);
    expect(load.fights).toEqual([]);
    expect(load.skipped[0]!.reason).toContain('turnTimesMs[0]');
  });

  it('takes the preset from the loadout, not from what the replay says about itself', { timeout: BOT_FIGHT_TIMEOUT_MS }, () => {
    const rec = play(1, { preset: 'tech', tester: 'P1', attempt: 1 });
    const f = analyseFight(C, rec, 'x');
    expect(typeof f === 'string' ? f : f.preset).toBe('hybrid');
  });

  it('leaves out the testers it is told to', { timeout: BOT_FIGHT_TIMEOUT_MS }, () => {
    const root = tmp();
    save(root, 'a', play(1, { preset: 'hybrid', tester: 'DEV', attempt: 1 }));
    save(root, 'b', play(2, { preset: 'hybrid', tester: 'P1', attempt: 1 }));
    const load = loadTelemetry(C, root, { excludeTesters: ['DEV'] });
    expect(load.fights.map((f) => f.tester)).toEqual(['P1']);
    expect(load.skipped).toEqual([{ name: 'a', reason: expect.stringContaining('DEV excluded') }]);
  });

  it('still shows every metric, as n/a, when nothing was counted', () => {
    const text = formatPlaytest('telemetry', { fights: [], skipped: [{ name: 'x', reason: 'unfinished' }] });
    expect(text).toContain('Fights counted: 0; skipped: 1');
    expect(text.match(/\[N\/A/g)).toHaveLength(8);
    expect(text).not.toContain('By tester');
  });

  it('refuses a fight with a tester ID but no attempt number', { timeout: BOT_FIGHT_TIMEOUT_MS }, () => {
    const root = tmp();
    save(root, 'a-no-attempt', play(1, { preset: 'hybrid', tester: 'P1' }));
    const load = loadTelemetry(C, root);
    expect(load.fights).toEqual([]);
    expect(load.skipped).toEqual([{ name: 'a-no-attempt', reason: 'has a tester ID but no attempt number' }]);
  });

  it('says what is wrong when the folder does not exist', () => {
    expect(() => loadTelemetry(C, path.join(os.tmpdir(), 'lab-escape-no-such-folder'))).toThrow(/No telemetry folder/);
  });
});
