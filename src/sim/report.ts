import type { Content } from '../content/types';
import type { Category } from '../telemetry/summary';
import { median } from '../util/stats';
import type { FightRow } from './batch';

const CATS: Category[] = ['player', 'minion', 'enemy', 'hazard'];

export interface LiftRow {
  pick: string;
  name: string;
  kind: 'ability' | 'upgrade';
  withN: number;
  withoutN: number;
  withWinRate: number;
  withoutWinRate: number;
  /** Win rate with the pick minus win rate without it, in percentage points. */
  lift: number;
}

export interface MetricCheck {
  metric: string;
  value: string;
  target: string;
  alarm: string;
  status: 'ok' | 'warn' | 'alarm' | 'n/a';
}

export interface BatchStats {
  fights: number;
  errors: number;
  wins: number;
  winRate: number;
  winRateCI: [number, number];
  roundsWin: { median: number; mean: number; min: number; max: number } | null;
  roundsLoss: { median: number; mean: number } | null;
  lossesReachingWave2: number;
  causesByCategory: Record<string, number>;
  topCauses: [string, number][];
  meanPlayerDamage: Record<Category, number>;
  meanFriendlyFireShare: number;
  meanHazardFires: number;
  fightsWithTwoHazardFires: number;
  meanPeakMinions: number;
  maxPeakMinions: number;
  wardenReachedShare: number | null;
  wardenEarlyDeathShare: number | null;
  abilityUse: Record<string, number>;
  lift: LiftRow[];
  checks: MetricCheck[];
}

const mean = (xs: number[]) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : 0);

/** Wilson score interval for a proportion (95%). */
export function wilson(k: number, n: number): [number, number] {
  if (n === 0) return [0, 0];
  const z = 1.96;
  const p = k / n;
  const den = 1 + (z * z) / n;
  const centre = (p + (z * z) / (2 * n)) / den;
  const half = (z * Math.sqrt((p * (1 - p)) / n + (z * z) / (4 * n * n))) / den;
  return [Math.max(0, centre - half), Math.min(1, centre + half)];
}

export function aggregate(rows: FightRow[], c: Content): BatchStats {
  const ok = rows.filter((r) => r.outcome !== 'error');
  const wins = ok.filter((r) => r.outcome === 'win');
  const losses = ok.filter((r) => r.outcome === 'lose');
  const causes: Record<string, number> = {};
  const sources: Record<string, number> = {};
  for (const r of losses) {
    const cat = r.deathCategory ?? (r.cause?.startsWith('Round') ? 'round cap' : 'other');
    causes[cat] = (causes[cat] ?? 0) + 1;
    if (r.deathSource) sources[r.deathSource] = (sources[r.deathSource] ?? 0) + 1;
  }
  const meanPlayerDamage = Object.fromEntries(CATS.map((k) => [k, mean(ok.map((r) => r.playerDamage[k]))])) as Record<Category, number>;
  const reached7 = ok.filter((r) => r.rounds >= 7 && r.wavesSpawned >= 3);
  const abilityUse: Record<string, number> = {};
  for (const r of ok) for (const [k, v] of Object.entries(r.abilitiesUsed)) abilityUse[k] = (abilityUse[k] ?? 0) + v;
  for (const k of Object.keys(abilityUse)) abilityUse[k] = abilityUse[k]! / Math.max(1, ok.length);
  const stats: BatchStats = {
    fights: rows.length,
    errors: rows.length - ok.length,
    wins: wins.length,
    winRate: ok.length ? wins.length / ok.length : 0,
    winRateCI: wilson(wins.length, ok.length),
    roundsWin: wins.length
      ? { median: median(wins.map((r) => r.rounds)), mean: mean(wins.map((r) => r.rounds)), min: Math.min(...wins.map((r) => r.rounds)), max: Math.max(...wins.map((r) => r.rounds)) }
      : null,
    roundsLoss: losses.length ? { median: median(losses.map((r) => r.rounds)), mean: mean(losses.map((r) => r.rounds)) } : null,
    lossesReachingWave2: losses.filter((r) => r.wavesSpawned >= 2).length,
    causesByCategory: causes,
    topCauses: Object.entries(sources).sort((a, b) => b[1] - a[1]).slice(0, 6),
    meanPlayerDamage,
    meanFriendlyFireShare: mean(ok.filter((r) => CATS.some((k) => r.playerDamage[k] > 0)).map((r) => r.friendlyFireShare)),
    meanHazardFires: mean(ok.map((r) => r.hazardFires)),
    fightsWithTwoHazardFires: ok.filter((r) => r.hazardFires >= 2).length,
    meanPeakMinions: mean(ok.map((r) => r.peakMinions)),
    maxPeakMinions: ok.length ? Math.max(...ok.map((r) => r.peakMinions)) : 0,
    wardenReachedShare: reached7.length ? reached7.filter((r) => r.wardenReachedPlayerRound !== null).length / reached7.length : null,
    wardenEarlyDeathShare: reached7.length ? reached7.filter((r) => r.wardenDiedRound !== null && r.wardenDiedRound < 9).length / reached7.length : null,
    abilityUse,
    lift: winRateLift(ok, c),
    checks: [],
  };
  stats.checks = metricChecks(stats, losses.length);
  return stats;
}

/** Win-rate lift of each ability and upgrade across a set of fights with varied loadouts. */
export function winRateLift(rows: FightRow[], c: Content): LiftRow[] {
  const loadouts = new Set(rows.map((r) => `${r.abilities.join(',')}|${r.upgrades.join(',')}`));
  if (loadouts.size < 2) return [];
  const out: LiftRow[] = [];
  const picks: [string, 'ability' | 'upgrade', string][] = [
    ...c.abilityOrder.map((id) => [id, 'ability', c.abilities[id]!.name] as [string, 'ability', string]),
    ...c.upgradeOrder.map((id) => [id, 'upgrade', c.upgrades[id]!.name] as [string, 'upgrade', string]),
  ];
  for (const [id, kind, name] of picks) {
    const has = (r: FightRow) => (kind === 'ability' ? r.abilities.includes(id) : r.upgrades.includes(id));
    const w = rows.filter(has);
    const wo = rows.filter((r) => !has(r));
    if (!w.length || !wo.length) continue;
    const wr = w.filter((r) => r.outcome === 'win').length / w.length;
    const wor = wo.filter((r) => r.outcome === 'win').length / wo.length;
    out.push({ pick: id, name, kind, withN: w.length, withoutN: wo.length, withWinRate: wr, withoutWinRate: wor, lift: (wr - wor) * 100 });
  }
  return out.sort((a, b) => b.lift - a.lift);
}

/** The spec's metric targets and alarms that a bot batch can speak to. */
function metricChecks(s: BatchStats, lossCount: number): MetricCheck[] {
  const pct = (x: number) => `${(x * 100).toFixed(1)}%`;
  const out: MetricCheck[] = [];
  if (s.roundsWin) {
    out.push({
      metric: 'Length of winning fights',
      value: `median ${s.roundsWin.median}, mean ${s.roundsWin.mean.toFixed(1)}`,
      target: '8 to 12',
      alarm: 'over 16',
      status: s.roundsWin.median > 16 ? 'alarm' : s.roundsWin.median < 8 || s.roundsWin.median > 12 ? 'warn' : 'ok',
    });
  }
  // The same per-fight mean the report prints as its friendly-fire headline, so the two never disagree.
  const own = s.meanFriendlyFireShare;
  out.push({
    metric: 'Share of player damage from own effects',
    value: pct(own),
    target: '5% to 30%',
    alarm: 'over 35%, or under 3%',
    status: own > 0.35 || own < 0.03 ? 'alarm' : own < 0.05 || own > 0.3 ? 'warn' : 'ok',
  });
  if (lossCount) {
    const shares = Object.entries(s.causesByCategory).map(([k, v]) => [k, v / lossCount] as const);
    const top = shares.sort((a, b) => b[1] - a[1])[0]!;
    const hazard = s.causesByCategory['hazard'] ?? 0;
    out.push({
      metric: 'Share of deaths from one source',
      value: `${top[0]} ${pct(top[1])}`,
      target: 'no source above 60%',
      alarm: 'hazards above 60%',
      status: hazard / lossCount > 0.6 ? 'alarm' : top[1] > 0.6 ? 'warn' : 'ok',
    });
  }
  out.push({
    metric: 'Hazard fires per fight',
    value: `${s.meanHazardFires.toFixed(2)} (${pct(s.fightsWithTwoHazardFires / Math.max(1, s.fights - s.errors))} of fights at least 2)`,
    target: 'at least 2',
    alarm: 'under 1 on average',
    status: s.meanHazardFires < 1 ? 'alarm' : s.meanHazardFires < 2 ? 'warn' : 'ok',
  });
  if (s.lift.length) {
    const top = s.lift[0]!;
    out.push({
      metric: 'Win-rate lift of each ability and upgrade',
      value: `max ${top.lift.toFixed(1)} points (${top.name})`,
      target: 'under 25 points',
      alarm: 'any above 40 points',
      status: top.lift > 40 ? 'alarm' : top.lift >= 25 ? 'warn' : 'ok',
    });
  }
  if (s.wardenReachedShare !== null) {
    out.push({
      metric: 'Warden reaches the player',
      value: `${pct(s.wardenReachedShare)} of fights that reach round 7; dies before round 9 in ${pct(s.wardenEarlyDeathShare ?? 0)}`,
      target: 'in most fights that reach round 7',
      alarm: 'dies before round 9 in most fights',
      status: (s.wardenEarlyDeathShare ?? 0) > 0.5 ? 'alarm' : s.wardenReachedShare < 0.5 ? 'warn' : 'ok',
    });
  }
  return out;
}

export function formatReport(title: string, s: BatchStats): string {
  const pct = (x: number) => `${(x * 100).toFixed(1)}%`;
  const L: string[] = [];
  L.push(`\n${title}`);
  L.push('='.repeat(title.length));
  L.push(`Fights: ${s.fights}${s.errors ? `  (errors: ${s.errors})` : ''}`);
  L.push(`Win rate: ${pct(s.winRate)}  (95% CI ${pct(s.winRateCI[0])} to ${pct(s.winRateCI[1])}), ${s.wins} wins`);
  if (s.roundsWin) L.push(`Rounds, wins:   median ${s.roundsWin.median}, mean ${s.roundsWin.mean.toFixed(1)}, range ${s.roundsWin.min} to ${s.roundsWin.max}`);
  if (s.roundsLoss) L.push(`Rounds, losses: median ${s.roundsLoss.median}, mean ${s.roundsLoss.mean.toFixed(1)}; ${s.lossesReachingWave2} reached wave 2`);
  const causes = Object.entries(s.causesByCategory).sort((a, b) => b[1] - a[1]);
  if (causes.length) L.push(`Causes of death: ${causes.map(([k, v]) => `${k} ${v}`).join(', ')}`);
  if (s.topCauses.length) L.push(`Top killers: ${s.topCauses.map(([k, v]) => `${k} ${v}`).join(', ')}`);
  L.push(`Mean damage to the player per fight: ${CATS.map((k) => `${k} ${s.meanPlayerDamage[k].toFixed(2)}`).join(', ')}`);
  L.push(`Mean friendly-fire share of the player's damage: ${pct(s.meanFriendlyFireShare)}`);
  L.push(`Hazard fires per fight: ${s.meanHazardFires.toFixed(2)}; peak minions: mean ${s.meanPeakMinions.toFixed(2)}, max ${s.maxPeakMinions}`);
  const use = Object.entries(s.abilityUse).sort((a, b) => b[1] - a[1]);
  if (use.length) L.push(`Uses per fight: ${use.map(([k, v]) => `${k} ${v.toFixed(1)}`).join(', ')}`);
  if (s.lift.length) {
    L.push('\nWin-rate lift (with minus without, points):');
    for (const r of s.lift) L.push(`  ${r.lift >= 0 ? '+' : ''}${r.lift.toFixed(1).padStart(5)}  ${r.name.padEnd(22)} ${r.kind.padEnd(8)} with ${pct(r.withWinRate)} (n=${r.withN}), without ${pct(r.withoutWinRate)} (n=${r.withoutN})`);
  }
  if (s.checks.length) {
    L.push('\nSpec metrics (bot proxy):');
    for (const ch of s.checks) L.push(`  [${ch.status.toUpperCase().padEnd(5)}] ${ch.metric}: ${ch.value}  (target ${ch.target}; alarm ${ch.alarm})`);
  }
  return L.join('\n');
}

/** One row per fight, for spreadsheets. */
export function rowsToCsv(rows: FightRow[]): string {
  const head = [
    'seed', 'bot', 'preset', 'abilities', 'upgrades', 'outcome', 'rounds', 'cause', 'death_category', 'death_source',
    ...CATS.map((k) => `player_dmg_${k}`), 'friendly_fire_share', ...CATS.map((k) => `enemy_dmg_${k}`),
    'hazard_fires', 'hazard_fires_hit_player', 'peak_minions', 'waves_spawned', 'warden_reached_player_round', 'warden_died_round', 'kills', 'commands', 'error',
  ];
  const esc = (v: unknown) => {
    const s = v === null || v === undefined ? '' : String(v);
    return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
  };
  const lines = [head.join(',')];
  for (const r of rows) {
    lines.push(
      [
        r.seed, r.bot, r.preset ?? '', r.abilities.join(' '), r.upgrades.join(' '), r.outcome, r.rounds, r.cause, r.deathCategory, r.deathSource,
        ...CATS.map((k) => r.playerDamage[k]), r.friendlyFireShare.toFixed(3), ...CATS.map((k) => r.enemyDamage[k]),
        r.hazardFires, r.hazardFiresHitPlayer, r.peakMinions, r.wavesSpawned, r.wardenReachedPlayerRound, r.wardenDiedRound, r.kills, r.commands, r.error,
      ].map(esc).join(','),
    );
  }
  return lines.join('\n') + '\n';
}

export function liftToCsv(lift: LiftRow[]): string {
  const lines = ['pick,name,kind,with_n,without_n,with_win_rate,without_win_rate,lift_points'];
  for (const r of lift) lines.push([r.pick, r.name, r.kind, r.withN, r.withoutN, r.withWinRate.toFixed(4), r.withoutWinRate.toFixed(4), r.lift.toFixed(2)].join(','));
  return lines.join('\n') + '\n';
}
