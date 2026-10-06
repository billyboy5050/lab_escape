import { afterEach, describe, expect, it, vi } from 'vitest';
import { loadTester, nextAttempt, saveTelemetry } from '../src/client/telemetry';
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
  it('numbers attempts after the fights a tester has finished, per tester', async () => {
    storage();
    vi.stubGlobal('fetch', () => Promise.resolve({ ok: false }));
    const finish = async (tester: string) => {
      const s = new FightSession(C, { abilities: ['sidearm'], upgrades: [] }, {}, { preset: 'hybrid', tester, attempt: nextAttempt(tester) });
      await saveTelemetry(C, s);
    };
    expect(nextAttempt('P3')).toBe(1);
    await finish('P3');
    await finish('P3');
    expect(nextAttempt('P3')).toBe(3);
    expect(nextAttempt('P4')).toBe(1);
  });
  it('treats a corrupt attempt count as zero', () => {
    storage({ 'lab-escape.attemptCounts': '{"P3":"many"}' });
    expect(nextAttempt('P3')).toBe(1);
  });
});
