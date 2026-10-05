import { describe, expect, it } from 'vitest';
import type { FightRow } from '../src/sim/batch';
import { aggregate, formatReport, formatTuneRow } from '../src/sim/report';
import { C } from './helpers';

const zero = { player: 0, minion: 0, enemy: 0, hazard: 0 };

function row(seed: number, playerDamage: FightRow['playerDamage'], friendlyFireShare: number): FightRow {
  return {
    seed, bot: 'greedy', preset: 'hybrid', abilities: [], upgrades: [], outcome: 'win', rounds: 10, cause: null, deathCategory: null, deathSource: null,
    playerDamage, friendlyFireShare, enemyDamage: { ...zero }, hazardFires: 2, hazardFiresHitPlayer: 0, peakMinions: 1, wavesSpawned: 3,
    wardenReachedPlayerRound: null, wardenDiedRound: null, kills: 0, commands: 0, abilitiesUsed: {},
  };
}

describe('friendly-fire share in the batch report', () => {
  // One fight where 90% of a small amount of damage was friendly, one with a lot of enemy damage and none.
  // The per-fight mean is 45%; pooling the damage first would give 8.2%.
  const rows = [row(1, { player: 9, minion: 0, enemy: 1, hazard: 0 }, 0.9), row(2, { player: 0, minion: 0, enemy: 100, hazard: 0 }, 0)];

  it('uses the same figure for the headline and the alarm check', () => {
    const stats = aggregate(rows, C);
    const check = stats.checks.find((k) => k.metric === 'Share of player damage from own effects')!;
    expect(stats.meanFriendlyFireShare).toBeCloseTo(0.45);
    expect(check.value).toBe('45.0%');
    expect(check.status).toBe('alarm');
    expect(formatReport('t', stats)).toContain("Mean friendly-fire share of the player's damage: 45.0%");
  });
});

describe('tune rows', () => {
  const stats = (rows: FightRow[]) => aggregate(rows, C);
  const ok = (seed: number, outcome: 'win' | 'lose') => ({ ...row(seed, { ...zero }, 0), outcome });
  const failed = (seed: number, error: string): FightRow => ({ ...row(seed, { ...zero }, 0), outcome: 'error', error });

  it('shows one line of figures for a value whose fights all finished', () => {
    const lines = formatTuneRow('16', stats([ok(1, 'win'), ok(2, 'lose')]));
    expect(lines).toHaveLength(1);
    expect(lines[0]).toMatch(/^16\s+50\.0% \(\d+ to \d+\)/);
  });

  it('flags a value where only some fights errored, so survivors are not read as the whole result', () => {
    const lines = formatTuneRow('0', stats([ok(1, 'win'), ok(2, 'win'), failed(3, 'Unknown unit "x"')]), 'Unknown unit "x"');
    expect(lines[0]).toMatch(/^0\s+100\.0%/);
    expect(lines[0]).toContain('1 of 3 fights errored');
    expect(lines.join('\n')).toContain('Unknown unit "x"');
    expect(lines.join('\n')).toMatch(/the figures cover the 2 that finished/);
  });

  it('shows no figures at all when every fight errored, rather than a win rate of 0%', () => {
    const lines = formatTuneRow('-1', stats([failed(1, 'boom'), failed(2, 'boom')]), 'boom');
    expect(lines[0]).toMatch(/^-1\s+all 2 fights errored/);
    expect(lines[0]).not.toContain('%');
    expect(lines.join('\n')).toContain('boom');
  });
});
