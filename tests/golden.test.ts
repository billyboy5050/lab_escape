import fs from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import { runBatch, seedRange } from '../src/sim/batch';
import { verifyGolden, type GoldenFile } from '../src/sim/golden';
import type { GameEvent } from '../src/state/types';
import { BOT_FIGHT_TIMEOUT_MS, C } from './helpers';

describe('golden replays', () => {
  for (const p of C.presets) {
    it(`${p.name} preset: the event log has not changed`, () => {
      const file = path.join('golden', `${p.id}.json`);
      expect(fs.existsSync(file), `${file} is missing: run npm run golden:record`).toBe(true);
      const g = JSON.parse(fs.readFileSync(file, 'utf8')) as GoldenFile;
      const evFile = path.join('golden', `${p.id}.events.jsonl`);
      const expected = fs.existsSync(evFile) ? fs.readFileSync(evFile, 'utf8').trim().split('\n').map((l) => JSON.parse(l) as GameEvent) : undefined;
      const r = verifyGolden(C, g, expected);
      expect(r.problem ?? 'ok').toBe('ok');
    });
  }
});

describe('batch runs', () => {
  it('the same batch with the same seeds gives identical results', () => {
    const spec = { bot: 'random' as const, loadout: { sweep: true as const }, seeds: seedRange(1, 40) };
    expect(runBatch(spec)).toEqual(runBatch(spec));
  });

  it('greedy batches are reproducible too', () => {
    const spec = { bot: 'greedy' as const, loadout: { preset: 'alien' }, seeds: seedRange(1, 3) };
    expect(runBatch(spec)).toEqual(runBatch(spec));
  }, BOT_FIGHT_TIMEOUT_MS);

  it('random bots find no crashes or infinite loops across every preset', () => {
    for (const p of C.presets) {
      const rows = runBatch({ bot: 'random', loadout: { preset: p.id }, seeds: seedRange(1, 60) });
      expect(rows.filter((r) => r.outcome === 'error').map((r) => r.error)).toEqual([]);
    }
  });
});
