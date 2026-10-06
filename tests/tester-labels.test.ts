import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadTester, saveTelemetry } from '../src/client/telemetry';
import { cleanTester, MAX_TESTER_LENGTH, telemetryFolderName } from '../src/telemetry/label';
import { newRecord, recordProblems } from '../src/telemetry/record';
import { FightSession } from '../src/telemetry/session';
import { telemetryDir } from '../src/dev/telemetryPath';
import { C } from './helpers';

function storage(data: Record<string, string> = {}) {
  const store = new Map(Object.entries(data));
  vi.stubGlobal('localStorage', {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => void store.set(k, v),
    removeItem: (k: string) => void store.delete(k),
  });
  return store;
}
afterEach(() => vi.unstubAllGlobals());

const T0 = new Date('2026-10-06T12:00:00.123Z');

describe('cleanTester', () => {
  it('keeps a plain ID and trims it', () => expect(cleanTester(' P3 ')).toBe('P3'));
  it('replaces anything that is not a word character or dash', () => expect(cleanTester('P3/../x y')).toBe('P3____x_y'));
  it('cuts long IDs', () => expect(cleanTester('x'.repeat(50))).toHaveLength(MAX_TESTER_LENGTH));
  it.each([undefined, null, 5, '', '   '])('treats %j as no tester', (v) => expect(cleanTester(v)).toBeUndefined());
});

describe('telemetryFolderName', () => {
  it('is unchanged without a tester', () => expect(telemetryFolderName(T0, { preset: 'hybrid' }, 'win')).toBe('2026-10-06T12-00-00-123Z-hybrid-win'));
  it('adds the tester and attempt after the time', () => expect(telemetryFolderName(T0, { preset: 'hybrid', tester: 'P3', attempt: 2 }, 'lose')).toBe('2026-10-06T12-00-00-123Z-P3-a2-hybrid-lose'));
  it('falls back to custom without a preset', () => expect(telemetryFolderName(T0, { tester: 'P3' }, 'win')).toBe('2026-10-06T12-00-00-123Z-P3-custom-win'));
  it('stays inside the telemetry folder even for a hostile tester', () => {
    const name = telemetryFolderName(T0, { tester: '../../etc' }, 'win');
    expect(telemetryDir('/srv/telemetry', name)).toMatch(/^\/srv\/telemetry\/[^/]+$/);
  });
});

describe('replay meta checks', () => {
  const rec = (meta: unknown) => ({ ...newRecord(C, { abilities: ['sidearm'], upgrades: [] }), meta });
  it('accepts a tester and an attempt', () => expect(recordProblems(rec({ tester: 'P3', attempt: 1 }))).toEqual([]));
  it.each([[{ tester: 3 }], [{ attempt: 0 }], [{ attempt: 1.5 }], [{ attempt: '2' }]])('rejects %j', (meta) => expect(recordProblems(rec(meta))).not.toEqual([]));
});

describe('tester and attempt in the browser', () => {
  it('takes the tester from the URL and remembers it', () => {
    const store = storage();
    expect(loadTester('?tester=P3')).toBe('P3');
    expect(store.get('lab-escape.tester')).toBe('"P3"');
    expect(loadTester('')).toBe('P3');
  });
  it('is undefined with no URL parameter and nothing saved, or a corrupt saved value', () => {
    storage();
    expect(loadTester('')).toBeUndefined();
    storage({ 'lab-escape.tester': '{"x":1}' });
    expect(loadTester('')).toBeUndefined();
  });
  const session = (tester?: string) => new FightSession(C, { abilities: ['sidearm'], upgrades: [] }, {}, { preset: 'hybrid', tester });
  const finish = async (s: FightSession) => {
    vi.stubGlobal('fetch', () => Promise.resolve({ ok: false }));
    await saveTelemetry(C, s);
    return s.record.meta?.attempt;
  };

  it('numbers attempts after the fights a tester has finished, per tester', async () => {
    storage();
    expect(await finish(session('P3'))).toBe(1);
    expect(await finish(session('P3'))).toBe(2);
    expect(await finish(session('P4'))).toBe(1);
  });
  it('numbers by the counter at the finish, so two fights open at once get different numbers', async () => {
    storage();
    const a = session('P3');
    const b = session('P3');
    expect(await finish(b)).toBe(1);
    expect(await finish(a)).toBe(2);
  });
  it('gives a fight with no tester no attempt number', async () => {
    storage();
    expect(await finish(session())).toBeUndefined();
  });
  it.each(['constructor', 'toString', '__proto__', 'hasOwnProperty'])('numbers a tester called %s from 1, then 2', async (id) => {
    storage();
    expect(await finish(session(id))).toBe(1);
    expect(await finish(session(id))).toBe(2);
  });
  it('treats a corrupt attempt count as zero', async () => {
    storage({ 'lab-escape.attemptCounts': '{"P3":"many"}' });
    expect(await finish(session('P3'))).toBe(1);
  });
  it('keeps counting on the page when storage refuses writes', async () => {
    vi.stubGlobal('localStorage', {
      getItem: () => null,
      setItem: () => {
        throw new Error('storage is full');
      },
    });
    expect(await finish(session('Q1'))).toBe(1);
    expect(await finish(session('Q1'))).toBe(2);
    expect(await finish(session('Q2'))).toBe(1);
  });
  it('treats a count that cannot be incremented safely as corrupt', async () => {
    storage({ 'lab-escape.attemptCounts': '{"P3":9007199254740991}' });
    expect(await finish(session('P3'))).toBe(1);
  });
});
