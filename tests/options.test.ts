import { describe, expect, it } from 'vitest';
import type { BatchSpec } from '../src/sim/batch';
import { parseInteger } from '../src/sim/options';
import { runBatchParallel } from '../src/sim/parallel';

describe('parseInteger', () => {
  it('returns undefined for an option that was not given', () => {
    expect(parseInteger('workers', undefined, { min: 1 })).toBeUndefined();
  });

  it('accepts whole numbers', () => {
    expect(parseInteger('workers', '4', { min: 1 })).toBe(4);
    expect(parseInteger('seeds', '1000', { min: 1 })).toBe(1000);
    expect(parseInteger('from', '0')).toBe(0);
    expect(parseInteger('from', '-3')).toBe(-3);
  });

  it.each(['foo', '', ' ', '2.5', 'Infinity', 'NaN', '1e999', '4x'])('rejects %j, which would otherwise reach the pool as NaN or a fraction', (raw) => {
    expect(() => parseInteger('workers', raw, { min: 1 })).toThrow(/--workers must be a whole number of at least 1/);
  });

  it('rejects a number below the minimum and names the option and the value', () => {
    expect(() => parseInteger('seeds', '0', { min: 1 })).toThrow('--seeds must be a whole number of at least 1 (got "0")');
  });

  it('says only "whole number" when there is no minimum', () => {
    expect(() => parseInteger('from', 'abc')).toThrow('--from must be a whole number (got "abc")');
  });
});

describe('runBatchParallel', () => {
  const spec: BatchSpec = { bot: 'greedy', loadout: { preset: 'hybrid' }, seeds: [1, 2, 3, 4] };

  it.each([NaN, 0, -1, 1.5, Infinity])('refuses %s workers instead of starting a pool that can never finish', async (workers) => {
    await expect(runBatchParallel(spec, { workers })).rejects.toThrow(/workers must be a whole number of at least 1/);
  });
});
