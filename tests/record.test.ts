import { describe, expect, it } from 'vitest';
import { ENGINE_VERSION } from '../src/state/state';
import { checkCompatible, newRecord, recordProblems, replay, ReplayMismatch, type FightRecord } from '../src/telemetry/record';
import fs from 'node:fs';
import { makeBot } from '../src/sim/batch';
import { runFight } from '../src/sim/runner';
import { C } from './helpers';

const LOADOUT = { abilities: ['sidearm', 'proximity_mine'], upgrades: [] };

function good(): FightRecord {
  const rec = newRecord(C, LOADOUT, { maxWaves: 2 }, { created: '2026-10-04T00:00:00.000Z', label: 'client', preset: 'tech', turnTimesMs: [4000, 6500] });
  rec.commands.push({ type: 'move', path: ['B7'] }, { type: 'endTurn' }, { type: 'debug', op: 'forceWave' });
  return rec;
}

/** A record with one piece of the file replaced, the way a hand-edited or truncated file would look. */
function broken(edit: (r: Record<string, unknown>) => void): unknown {
  const r = JSON.parse(JSON.stringify(good())) as Record<string, unknown>;
  edit(r);
  return r;
}

const message = (raw: unknown): string => {
  try {
    checkCompatible(C, raw);
  } catch (e) {
    expect(e).toBeInstanceOf(ReplayMismatch);
    return (e as Error).message;
  }
  throw new Error('expected the record to be rejected');
};

describe('replay file shape', () => {
  it('accepts a record the app wrote, with or without its metadata', () => {
    expect(recordProblems(good())).toEqual([]);
    expect(() => checkCompatible(C, JSON.parse(JSON.stringify(good())))).not.toThrow();
    expect(recordProblems(broken((r) => delete r.meta))).toEqual([]);
    expect(recordProblems(broken((r) => (r.meta = { bot: 'greedy', seed: 7 })))).toEqual([]);
  });

  it.each([null, 'replay', 7, [], undefined])('rejects %j, which is not a replay file at all', (raw) => {
    expect(message(raw)).toMatch(/not a replay file/i);
  });

  it.each([
    ['loadout is missing', (r: Record<string, unknown>) => delete r.loadout, 'loadout'],
    ['loadout is null', (r: Record<string, unknown>) => (r.loadout = null), 'loadout'],
    ['loadout has no abilities', (r: Record<string, unknown>) => (r.loadout = { upgrades: [] }), 'loadout.abilities'],
    ['loadout upgrades are not a list', (r: Record<string, unknown>) => (r.loadout = { abilities: [], upgrades: 'x' }), 'loadout.upgrades'],
    ['an ability is not a name', (r: Record<string, unknown>) => (r.loadout = { abilities: ['sidearm', 3], upgrades: [] }), 'loadout.abilities[1]'],
    ['settings is missing', (r: Record<string, unknown>) => delete r.settings, 'settings'],
    ['settings is a list', (r: Record<string, unknown>) => (r.settings = []), 'settings'],
    ['maxWaves is not a number', (r: Record<string, unknown>) => (r.settings = { maxWaves: '2' }), 'settings.maxWaves'],
    ['maxWaves is zero', (r: Record<string, unknown>) => (r.settings = { maxWaves: 0 }), 'settings.maxWaves'],
    ['commands is missing', (r: Record<string, unknown>) => delete r.commands, 'commands'],
    ['commands is not a list', (r: Record<string, unknown>) => (r.commands = { length: 3 }), 'commands'],
    ['a command is null', (r: Record<string, unknown>) => (r.commands = [{ type: 'endTurn' }, null]), 'commands[1]'],
    ['a command has no type', (r: Record<string, unknown>) => (r.commands = [{ path: ['B7'] }]), 'commands[0]'],
    ['a command has an unknown type', (r: Record<string, unknown>) => (r.commands = [{ type: 'teleport' }]), 'commands[0]'],
    ['a debug command has an unknown op', (r: Record<string, unknown>) => (r.commands = [{ type: 'debug', op: 'godMode' }]), 'commands[0]'],
    ['meta is not an object', (r: Record<string, unknown>) => (r.meta = 'x'), 'meta'],
    ['turn times are not a list', (r: Record<string, unknown>) => (r.meta = { turnTimesMs: 5 }), 'meta.turnTimesMs'],
    ['a turn time is not a number', (r: Record<string, unknown>) => (r.meta = { turnTimesMs: [1, 'slow'] }), 'meta.turnTimesMs[1]'],
  ])('rejects a file where %s, naming the field, instead of crashing the screen later', (_, edit, field) => {
    const raw = broken(edit);
    expect(recordProblems(raw).join('\n')).toContain(field);
    expect(message(raw)).toContain(field);
  });

  it('checks the format, engine version and content hash first, so an old file gets the useful message', () => {
    expect(message(broken((r) => ((r.engineVersion = '0.0.1'), delete r.loadout)))).toMatch(/recorded on engine 0\.0\.1; this is /);
    expect(message(broken((r) => ((r.contentHash = 'zzzz'), delete r.commands)))).toMatch(/recorded with content zzzz/);
    expect(message(broken((r) => ((r.format = 'other@2'), delete r.settings)))).toMatch(/Unknown replay format "other@2"/);
    expect(good().engineVersion).toBe(ENGINE_VERSION);
  });

  it('lists a few problems and says there are more, rather than printing one per bad command', () => {
    const text = message(broken((r) => (r.commands = Array.from({ length: 50 }, () => ({ type: 'nope' })))));
    expect(text.split('\n').length).toBeLessThan(12);
    expect(text).toMatch(/further problems not shown/);
  });

  it('keeps replay() from throwing a TypeError on a malformed file', () => {
    const raw = broken((r) => delete r.commands) as FightRecord;
    expect(() => replay(C, raw)).toThrow(ReplayMismatch);
  });
});

describe('replay command payloads', () => {
  const withCommand = (cmd: unknown) => broken((r) => (r.commands = [cmd]));

  it.each([
    ['a debug setAp with a text AP', { type: 'debug', op: 'setAp', ap: 'oops' }, 'commands[0].ap must be a number (got "oops")'],
    ['a debug setAp with no AP', { type: 'debug', op: 'setAp' }, 'commands[0].ap must be a number'],
    ['a debug setAp with a null AP (what NaN becomes in JSON)', { type: 'debug', op: 'setAp', ap: null }, 'commands[0].ap must be a number'],
    ['a debug setAp with text movement', { type: 'debug', op: 'setAp', ap: 3, movement: 'x' }, 'commands[0].movement must be a number'],
    ['a debug setHp with a text unit', { type: 'debug', op: 'setHp', unit: '1', value: 5 }, 'commands[0].unit must be a whole number'],
    ['a debug setHp with a fractional unit', { type: 'debug', op: 'setHp', unit: 1.5, value: 5 }, 'commands[0].unit must be a whole number'],
    ['a debug setHp with no value', { type: 'debug', op: 'setHp', unit: 1 }, 'commands[0].value must be a number'],
    ['a debug spawn with no unit name', { type: 'debug', op: 'spawn', target: 'D5' }, 'commands[0].def must be text'],
    ['a move whose path is not a list', { type: 'move', path: 'B7' }, 'commands[0].path must be a list of tile names'],
    ['a move whose path holds a number', { type: 'move', path: ['B7', 3] }, 'commands[0].path must be a list of tile names'],
    ['a move with no path', { type: 'move' }, 'commands[0].path must be a list of tile names'],
    ['an ability with no name', { type: 'ability', target: 'B5' }, 'commands[0].ability must be text'],
    ['an ability with a numeric target', { type: 'ability', ability: 'sidearm', target: 5 }, 'commands[0].target must be text'],
    ['an ability with a bad second target', { type: 'ability', ability: 'barrier_shield', target: 'E5', target2: {} }, 'commands[0].target2 must be text'],
    ['an ability with an unknown grapple mode', { type: 'ability', ability: 'grapple_hook', target: 'B5', mode: 'sideways' }, 'commands[0].mode must be "self" or "unit"'],
    ['a reload with no target', { type: 'reload' }, 'commands[0].target must be text'],
    ['a pick-up with a numeric target', { type: 'pickUpMine', target: 7 }, 'commands[0].target must be text'],
    ['a redeploy with a fractional drone id', { type: 'redeploy', drone: 1.5, target: 'C7' }, 'commands[0].drone must be a whole number'],
  ])('rejects %s, which the engine would not refuse (or would turn into NaN)', (_name, cmd, problem) => {
    expect(recordProblems(withCommand(cmd))).toContainEqual(expect.stringContaining(problem));
    expect(message(withCommand(cmd))).toMatch(/malformed/);
  });

  it.each([
    { type: 'sprint' },
    { type: 'endTurn' },
    { type: 'move', path: ['B7', 'B6'] },
    { type: 'ability', ability: 'sidearm', target: 'B5' },
    { type: 'ability', ability: 'barrier_shield', target: 'E5', target2: 'D6' },
    { type: 'ability', ability: 'sidearm', target: 'B5', target2: null },
    { type: 'ability', ability: 'grapple_hook', target: 'B5', mode: 'self' },
    { type: 'ability', ability: 'grapple_hook', target: 'B5', mode: 'unit' },
    { type: 'reload', target: 'C6' },
    { type: 'pickUpMine', target: 'B7' },
    { type: 'redeploy', drone: 4, target: 'C7' },
    { type: 'debug', op: 'setHp', unit: 0, value: 3 },
    { type: 'debug', op: 'setAp', ap: 5 },
    { type: 'debug', op: 'setAp', ap: 5, movement: 4 },
    { type: 'debug', op: 'spawn', def: 'guard', target: 'D5' },
    { type: 'debug', op: 'forceWave' },
  ])('accepts %j', (cmd) => {
    expect(recordProblems(withCommand(cmd))).toEqual([]);
  });

  it('accepts every replay file the repository ships', () => {
    const files = fs.readdirSync('reports').filter((f) => /^winning-line-.*\.json$/.test(f));
    expect(files.length).toBeGreaterThan(0);
    for (const f of files) expect(recordProblems(JSON.parse(fs.readFileSync(`reports/${f}`, 'utf8'))), f).toEqual([]);
  });

  it('accepts the records of real bot fights, which use every command the player can issue', () => {
    for (const p of C.presets) {
      for (const seed of [1, 2]) {
        const { record } = runFight(C, { abilities: p.abilities, upgrades: p.upgrades }, makeBot('greedy', seed));
        expect(record.commands.length).toBeGreaterThan(5);
        expect(recordProblems(JSON.parse(JSON.stringify(record))), `${p.id} seed ${seed}`).toEqual([]);
      }
    }
  });
});
