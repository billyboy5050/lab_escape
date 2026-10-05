import { afterEach, describe, expect, it, vi } from 'vitest';
import { isLoadout, isPresetId, reconcileLoadout } from '../src/client/loadoutSync';
import { loadAttempts, loadPref } from '../src/client/telemetry';
import { C } from './helpers';

function storage(data: Record<string, string>, opts: { throws?: boolean } = {}) {
  const store = new Map(Object.entries(data));
  vi.stubGlobal('localStorage', {
    getItem: (k: string) => {
      if (opts.throws) throw new Error('storage is blocked');
      return store.get(k) ?? null;
    },
    setItem: (k: string, v: string) => void store.set(k, v),
    removeItem: (k: string) => void store.delete(k),
  });
}
afterEach(() => vi.unstubAllGlobals());

const FALLBACK = { abilities: ['sidearm'], upgrades: [] };
const stored = (v: unknown) => ({ 'lab-escape.loadout': JSON.stringify(v) });

describe('loadPref', () => {
  it('returns a stored value that passes the check', () => {
    storage(stored({ abilities: ['sidearm', 'lunge'], upgrades: ['T1_piercing_rounds'] }));
    expect(loadPref('loadout', FALLBACK, isLoadout)).toEqual({ abilities: ['sidearm', 'lunge'], upgrades: ['T1_piercing_rounds'] });
  });

  it('returns the fallback when nothing is stored', () => {
    storage({});
    expect(loadPref('loadout', FALLBACK, isLoadout)).toBe(FALLBACK);
  });

  it.each([
    ['an object of another shape', { foo: 1 }],
    ['abilities that are not a list', { abilities: 'sidearm', upgrades: [] }],
    ['upgrades that are missing', { abilities: ['sidearm'] }],
    ['a name that is not text', { abilities: ['sidearm', 3], upgrades: [] }],
    ['null', null],
    ['a list', ['sidearm']],
    ['a bare string', 'tech'],
    ['a number', 7],
  ])('falls back instead of returning %s, which would crash startup', (_name, value) => {
    storage(stored(value));
    const l = loadPref('loadout', FALLBACK, isLoadout);
    expect(l).toBe(FALLBACK);
    expect(() => reconcileLoadout(C, l, null)).not.toThrow();
  });

  it('falls back on text that is not JSON, and when storage itself throws', () => {
    storage({ 'lab-escape.loadout': '{not json' });
    expect(loadPref('loadout', FALLBACK, isLoadout)).toBe(FALLBACK);
    storage({}, { throws: true });
    expect(loadPref('loadout', FALLBACK, isLoadout)).toBe(FALLBACK);
  });

  it('reads the preset as an id or null, and falls back for anything else', () => {
    storage({ 'lab-escape.preset': JSON.stringify('tech') });
    expect(loadPref('preset', 'hybrid', isPresetId)).toBe('tech');
    storage({ 'lab-escape.preset': 'null' });
    expect(loadPref<string | null>('preset', 'hybrid', isPresetId)).toBeNull();
    for (const bad of [42, {}, ['tech'], true]) {
      storage({ 'lab-escape.preset': JSON.stringify(bad) });
      expect(loadPref<string | null>('preset', 'hybrid', isPresetId)).toBe('hybrid');
    }
  });
});

describe('loadAttempts', () => {
  const attempt = { at: '2026-10-05T10:00:00.000Z', preset: 'tech', loadout: FALLBACK, outcome: 'win', rounds: 9, cause: null };
  const attempts = (v: unknown) => ({ 'lab-escape.attempts': JSON.stringify(v) });

  it('returns the stored attempts', () => {
    storage(attempts([attempt, { ...attempt, preset: null, outcome: 'lose' }]));
    expect(loadAttempts()).toHaveLength(2);
  });

  it('is empty when nothing is stored, or the value is not a list, or not JSON, or storage throws', () => {
    storage({});
    expect(loadAttempts()).toEqual([]);
    for (const v of [{}, 'x', 5, null]) {
      storage(attempts(v));
      expect(loadAttempts()).toEqual([]);
    }
    storage({ 'lab-escape.attempts': '[oops' });
    expect(loadAttempts()).toEqual([]);
    storage({}, { throws: true });
    expect(loadAttempts()).toEqual([]);
  });

  it('drops entries the loadout screen could not draw, and keeps the rest', () => {
    storage(attempts([attempt, null, 3, 'x', { outcome: 'win' }, { ...attempt, rounds: '9' }, { ...attempt, at: 5 }, { ...attempt, preset: 7 }, attempt]));
    expect(loadAttempts()).toEqual([attempt, attempt]);
  });
});

describe('isLoadout and isPresetId', () => {
  it('check shape only, so a loadout naming a removed ability is still a loadout', () => {
    expect(isLoadout({ abilities: ['gone'], upgrades: ['gone'] })).toBe(true);
    expect(isLoadout({ abilities: [], upgrades: [] })).toBe(true);
    expect(isPresetId('no_such_preset')).toBe(true);
  });
});
