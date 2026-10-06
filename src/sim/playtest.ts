import fs from 'node:fs';
import path from 'node:path';
import type { Content } from '../content/types';
import { checkCompatible, replay, type FightRecord } from '../telemetry/record';
import { summarize, type Category } from '../telemetry/summary';
import { median } from '../util/stats';
import type { MetricCheck } from './report';

/** What the playtest metrics need from one saved fight. */
export interface PlaytestFight {
  /** The telemetry folder it came from. */
  name: string;
  tester?: string;
  attempt?: number;
  preset?: string;
  outcome: 'win' | 'lose';
  rounds: number;
  /** Share of the player's damage that came from the player's own side (0 to 1). */
  friendlyFireShare: number;
  /** Total damage the player took; a fight with none has no friendly-fire share to average. */
  playerDamage: number;
  causeCategory: Category | null;
  hazardFires: number;
  /** Seconds the player took on each turn that ended with a full swarm (minions at the cap). */
  fullSwarmTurnSeconds: number[];
}

export interface TelemetryLoad {
  fights: PlaytestFight[];
  /** Folders left out, with the reason: not a replay, an old engine or content version, unfinished, or played with debug commands. */
  skipped: { name: string; reason: string }[];
}

/**
 * Reads every `<DIR>/<fight>/replay.json` and replays it, so every figure comes from the fight itself and a replay from
 * another engine or content version is refused, not misread. Fights that did not finish, or used debug commands (a
 * cheat is not an attempt), are skipped and reported.
 */
export function loadTelemetry(c: Content, dir: string): TelemetryLoad {
  if (!fs.existsSync(dir)) throw new Error(`No telemetry folder at ${dir}. Play some fights with npm run dev first.`);
  const out: TelemetryLoad = { fights: [], skipped: [] };
  for (const entry of fs.readdirSync(dir, { withFileTypes: true }).filter((e) => e.isDirectory()).sort((a, b) => a.name.localeCompare(b.name))) {
    const file = path.join(dir, entry.name, 'replay.json');
    if (!fs.existsSync(file)) {
      out.skipped.push({ name: entry.name, reason: 'no replay.json' });
      continue;
    }
    try {
      const rec: unknown = JSON.parse(fs.readFileSync(file, 'utf8'));
      checkCompatible(c, rec);
      const f = analyseFight(c, rec, entry.name);
      if (typeof f === 'string') out.skipped.push({ name: entry.name, reason: f });
      else out.fights.push(f);
    } catch (e) {
      out.skipped.push({ name: entry.name, reason: e instanceof Error ? e.message.split('\n')[0]! : String(e) });
    }
  }
  return out;
}

/** The figures for one fight, or the reason it does not count. */
export function analyseFight(c: Content, rec: FightRecord, name: string): PlaytestFight | string {
  if (rec.commands.some((cmd) => cmd.type === 'debug')) return 'used debug commands';
  // The shortened fight (wave 3 held back) has another win condition, so it would skew every figure here.
  if (rec.settings.maxWaves !== undefined) return 'played with wave 3 held back';
  const r = replay(c, rec, { keepStates: true });
  const s = summarize(r.events, r.state);
  if (s.outcome === 'unfinished') return 'unfinished';
  // A turn ends at each End Turn, and at the action that ends the fight if that was not an End Turn. The client records
  // one time per turn, in that order, so turn k's time pairs with the state just before its last command.
  const ends: number[] = [];
  rec.commands.forEach((cmd, i) => cmd.type === 'endTurn' && ends.push(i));
  if (rec.commands.length && rec.commands[rec.commands.length - 1]!.type !== 'endTurn') ends.push(rec.commands.length - 1);
  const times = rec.meta?.turnTimesMs ?? [];
  const cap = c.rules.caps.minions;
  const fullSwarmTurnSeconds: number[] = [];
  ends.forEach((idx, k) => {
    const t = times[k];
    if (t === undefined) return;
    const minions = r.states[idx]!.units.filter((u) => u.kind === 'minion' && !u.dead).length;
    if (minions >= cap) fullSwarmTurnSeconds.push(t / 1000);
  });
  return {
    name,
    tester: rec.meta?.tester,
    attempt: rec.meta?.attempt,
    preset: rec.meta?.preset,
    outcome: s.outcome,
    rounds: s.rounds,
    friendlyFireShare: s.friendlyFireShare,
    playerDamage: s.playerDamageTaken.player + s.playerDamageTaken.minion + s.playerDamageTaken.enemy + s.playerDamageTaken.hazard,
    causeCategory: s.causeOfDeath?.category ?? null,
    hazardFires: s.hazardFires.length,
    fullSwarmTurnSeconds,
  };
}

export interface PlaytestOptions {
  /** The first attempt number counted as an informed attempt (round 3 follows round 2 in the same browser, so numbering carries on). */
  informedFrom: number;
  /** How many attempts per tester the informed round has (the spec: 3), counted from `informedFrom`. */
  informedAttempts: number;
  /** The preset the informed round plays. */
  informedPreset: string;
}

export const DEFAULT_PLAYTEST: PlaytestOptions = { informedFrom: 2, informedAttempts: 3, informedPreset: 'hybrid' };

const pct = (x: number) => `${(x * 100).toFixed(0)}%`;
const mean = (xs: readonly number[]) => (xs.length ? xs.reduce((a, b) => a + b, 0) / xs.length : 0);
const na = (metric: string, target: string, alarm: string, why: string): MetricCheck => ({ metric, value: why, target, alarm, status: 'n/a' });

/** The spec's "Metrics and alarms" that telemetry can answer, from the fights loaded. Same status words as the simulator's report. */
export function playtestChecks(fights: readonly PlaytestFight[], opts: PlaytestOptions = DEFAULT_PLAYTEST): MetricCheck[] {
  const out: MetricCheck[] = [];
  const losses = fights.filter((f) => f.outcome === 'lose');
  const wins = fights.filter((f) => f.outcome === 'win');

  // One first attempt per tester: attempt numbers are counted per browser, so a tester who plays on another browser, or
  // clears storage, can leave several fights numbered 1. The earliest counts (folder names begin with the time).
  const firstByTester = new Map<string, PlaytestFight>();
  for (const f of fights) if (f.tester !== undefined && f.attempt === 1 && !firstByTester.has(f.tester)) firstByTester.set(f.tester, f);
  const first = [...firstByTester.values()];
  const firstLost = first.filter((f) => f.outcome === 'lose');
  // A tester whose only recorded fight is a lost first attempt may have quit there (or may not have played again yet).
  const stopped = firstLost.filter((f) => !fights.some((g) => g.tester === f.tester && (g.attempt ?? 0) > 1));
  out.push(
    first.length
      ? {
          metric: 'First attempts lost',
          value: `${firstLost.length} of ${first.length} testers (${pct(firstLost.length / first.length)}); ${stopped.length} with no fight after the loss`,
          target: 'about 70% (4 of 5, up to 6 of 8)',
          alarm: 'fewer than half, or players quit after one loss',
          status: firstLost.length / first.length < 0.5 ? 'alarm' : firstLost.length / first.length < 0.6 || firstLost.length / first.length > 0.8 ? 'warn' : 'ok',
        }
      : na('First attempts lost', 'about 70% (4 of 5, up to 6 of 8)', 'fewer than half', 'no fights with a tester and attempt 1'),
  );

  if (firstLost.length) {
    const m = median(firstLost.map((f) => f.rounds));
    out.push({
      metric: 'First-time losses that reach wave 2',
      value: `median loss in round ${m}`,
      target: 'most reach round 4',
      alarm: 'median loss before round 3',
      status: m < 3 ? 'alarm' : m < 4 ? 'warn' : 'ok',
    });
  } else out.push(na('First-time losses that reach wave 2', 'most reach round 4', 'median loss before round 3', 'no lost first attempts'));

  const informed = fights.filter((f) => f.preset === opts.informedPreset && f.attempt !== undefined && f.attempt >= opts.informedFrom && f.attempt < opts.informedFrom + opts.informedAttempts);
  if (informed.length) {
    const won = informed.filter((f) => f.outcome === 'win').length;
    const byAttempt = new Map<number, [number, number]>();
    for (const f of informed) {
      const [w, n] = byAttempt.get(f.attempt!) ?? [0, 0];
      byAttempt.set(f.attempt!, [w + (f.outcome === 'win' ? 1 : 0), n + 1]);
    }
    const detail = [...byAttempt].sort((a, b) => a[0] - b[0]).map(([a, [w, n]]) => `attempt ${a}: ${w}/${n}`).join(', ');
    const rate = won / informed.length;
    out.push({
      metric: `Informed win rate (${opts.informedPreset} preset, attempts ${opts.informedFrom} to ${opts.informedFrom + opts.informedAttempts - 1})`,
      value: `${won} of ${informed.length} (${pct(rate)}); ${detail}`,
      target: '30% to 50%',
      alarm: 'below 15% or above 70%',
      status: rate < 0.15 || rate > 0.7 ? 'alarm' : rate < 0.3 || rate > 0.5 ? 'warn' : 'ok',
    });
  } else out.push(na(`Informed win rate (${opts.informedPreset} preset)`, '30% to 50%', 'below 15% or above 70%', `no ${opts.informedPreset} fights at attempts ${opts.informedFrom} to ${opts.informedFrom + opts.informedAttempts - 1}`));

  if (wins.length) {
    const m = median(wins.map((f) => f.rounds));
    out.push({ metric: 'Length of winning fights', value: `median ${m} rounds (${wins.length} wins)`, target: '8 to 12', alarm: 'over 16', status: m > 16 ? 'alarm' : m < 8 || m > 12 ? 'warn' : 'ok' });
  } else out.push(na('Length of winning fights', '8 to 12', 'over 16', 'no wins'));

  const swarm = fights.flatMap((f) => f.fullSwarmTurnSeconds);
  if (swarm.length) {
    const m = median(swarm);
    out.push({ metric: 'Median turn time with a full swarm', value: `${m.toFixed(0)} s over ${swarm.length} turns`, target: 'under 60 s', alarm: 'over 90 s', status: m > 90 ? 'alarm' : m >= 60 ? 'warn' : 'ok' });
  } else out.push(na('Median turn time with a full swarm', 'under 60 s', 'over 90 s', 'no turn ended with the minion cap reached'));

  // A fight where the player took no damage has no share, and counting it as 0% would drag the mean down.
  const damaged = fights.filter((f) => f.playerDamage > 0);
  if (damaged.length) {
    const ff = mean(damaged.map((f) => f.friendlyFireShare));
    out.push({ metric: 'Share of player damage from own effects', value: `${pct(ff)} (${damaged.length} fights where the player took damage)`, target: '5% to 30%', alarm: 'over 35%, or under 3%', status: ff > 0.35 || ff < 0.03 ? 'alarm' : ff < 0.05 || ff > 0.3 ? 'warn' : 'ok' });
  }

  if (losses.length) {
    const counts = new Map<string, number>();
    for (const f of losses) counts.set(f.causeCategory ?? 'round cap', (counts.get(f.causeCategory ?? 'round cap') ?? 0) + 1);
    const ranked = [...counts].sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]));
    const top = ranked[0]!;
    const hazard = (counts.get('hazard') ?? 0) / losses.length;
    out.push({
      metric: 'Share of deaths from one source',
      value: ranked.map(([k, v]) => `${k} ${pct(v / losses.length)}`).join(', '),
      target: 'no source above 60%',
      alarm: 'hazards above 60%',
      status: hazard > 0.6 ? 'alarm' : top[1] / losses.length > 0.6 ? 'warn' : 'ok',
    });
  }

  if (fights.length) {
    const h = mean(fights.map((f) => f.hazardFires));
    out.push({ metric: 'Hazard fires per fight', value: h.toFixed(2), target: 'at least 2', alarm: 'under 1 on average', status: h < 1 ? 'alarm' : h < 2 ? 'warn' : 'ok' });
  }
  return out;
}

/** The report text: what was read, each metric against its target, then each tester's attempts. */
export function formatPlaytest(dir: string, load: TelemetryLoad, opts: PlaytestOptions = DEFAULT_PLAYTEST): string {
  const L: string[] = [];
  const title = `Playtest telemetry: ${dir}`;
  L.push(title, '='.repeat(title.length));
  L.push(`Fights counted: ${load.fights.length}; skipped: ${load.skipped.length}`);
  for (const s of load.skipped) L.push(`  skipped ${s.name}: ${s.reason}`);
  if (!load.fights.length) return L.join('\n');
  L.push('', 'Spec metrics:');
  for (const ch of playtestChecks(load.fights, opts)) L.push(`  [${ch.status.toUpperCase().padEnd(5)}] ${ch.metric}: ${ch.value}  (target ${ch.target}; alarm ${ch.alarm})`);
  const testers = new Map<string, PlaytestFight[]>();
  for (const f of load.fights) testers.set(f.tester ?? '(no tester)', [...(testers.get(f.tester ?? '(no tester)') ?? []), f]);
  L.push('', 'By tester:');
  for (const [t, fs_] of [...testers].sort((a, b) => a[0].localeCompare(b[0]))) {
    const line = [...fs_].sort((a, b) => (a.attempt ?? 0) - (b.attempt ?? 0)).map((f) => `${f.attempt !== undefined ? `a${f.attempt}` : '?'} ${f.preset ?? 'custom'} ${f.outcome} r${f.rounds}`).join(', ');
    L.push(`  ${t}: ${line}`);
  }
  return L.join('\n');
}
