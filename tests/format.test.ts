import { describe, expect, it } from 'vitest';
import { describeUnit } from '../src/client/format';
import { C, scenario } from './helpers';

describe('describeUnit', () => {
  const arrived = (round: number) => scenario({ round, units: [{ def: 'guard', at: 'C3', arrival: true }] });

  it('says a wave unit just arrived in the round it arrived', () => {
    const s = arrived(4);
    expect(describeUnit(C, s.units[1]!, s.round)).toContain('just arrived');
  });

  it('stops saying so in later rounds, though the unit keeps its arrival round', () => {
    const s = arrived(4);
    const guard = s.units[1]!;
    expect(guard.arrivalRound).toBe(4);
    expect(describeUnit(C, guard, 5)).not.toContain('just arrived');
    expect(describeUnit(C, guard, 9)).not.toContain('just arrived');
  });

  it('says nothing about arrival for a unit that was never an arrival', () => {
    const s = scenario({ units: [{ def: 'guard', at: 'C3' }] });
    expect(describeUnit(C, s.units[1]!, s.round)).not.toContain('just arrived');
  });
});
