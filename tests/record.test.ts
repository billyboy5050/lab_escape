import { describe, expect, it } from 'vitest';
import { ENGINE_VERSION } from '../src/state/state';
import { checkCompatible, newRecord, recordProblems, replay, ReplayMismatch, type FightRecord } from '../src/telemetry/record';
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
