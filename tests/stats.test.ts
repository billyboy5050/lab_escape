import { describe, expect, it } from 'vitest';
import { median } from '../src/util/stats';

describe('median', () => {
  it('takes the middle value of an odd count', () => {
    expect(median([30, 10, 20])).toBe(20);
  });

  it('averages the two middle values of an even count', () => {
    expect(median([10_000, 90_000])).toBe(50_000);
    expect(median([4, 1, 3, 2])).toBe(2.5);
  });

  it('does not reorder its input, and is 0 for an empty list', () => {
    const xs = [3, 1, 2];
    median(xs);
    expect(xs).toEqual([3, 1, 2]);
    expect(median([])).toBe(0);
  });
});
