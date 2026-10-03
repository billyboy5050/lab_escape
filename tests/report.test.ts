import { describe, expect, it } from 'vitest';
import type { FightRow } from '../src/sim/batch';
import { aggregate, formatReport } from '../src/sim/report';
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
