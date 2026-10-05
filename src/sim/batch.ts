import { buildContent, defaultContentFiles, withOverrides, type Content, type ContentFiles } from '../content';
import { upgradeAllowed } from '../content/load';
import type { FightSettings, Loadout } from '../state/types';
import type { Category, FightSummary } from '../telemetry/summary';
import type { FightRecord } from '../telemetry/record';
import { GreedyBot, RandomBot, type Bot } from './bots';
import { Rng } from './rng';
import { BotError, runFight } from './runner';

export type BotKind = 'random' | 'greedy' | 'greedy-naive';

export interface BatchSpec {
  bot: BotKind;
  /** A preset id, a fixed loadout, or a sweep of random valid loadouts (one per seed). */
  loadout: { preset: string } | { custom: Loadout } | { sweep: true; size?: number };
  seeds: number[];
  /** Tuning overrides applied to the content files, e.g. {"units.warden.hp": 16}. */
  overrides?: Record<string, unknown>;
  settings?: FightSettings;
  /** Keep each fight's command log in the rows (for saving winning lines). */
  keepRecords?: boolean;
}

export interface FightRow {
  seed: number;
  bot: string;
  preset: string | null;
  abilities: string[];
  upgrades: string[];
  outcome: FightSummary['outcome'] | 'error';
  rounds: number;
  cause: string | null;
  deathCategory: Category | null;
  deathSource: string | null;
  playerDamage: Record<Category, number>;
  friendlyFireShare: number;
  enemyDamage: Record<Category, number>;
  hazardFires: number;
  hazardFiresHitPlayer: number;
  peakMinions: number;
  wavesSpawned: number;
  wardenReachedPlayerRound: number | null;
  wardenDiedRound: number | null;
  kills: number;
  commands: number;
  abilitiesUsed: Record<string, number>;
  error?: string;
  record?: FightRecord;
}

export function contentFor(overrides?: Record<string, unknown>, files: ContentFiles = defaultContentFiles()): Content {
  return buildContent(overrides && Object.keys(overrides).length ? withOverrides(files, overrides) : files);
}

export function makeBot(kind: BotKind, seed: number): Bot {
  if (kind === 'random') return new RandomBot(seed);
  return new GreedyBot(seed, { heedCues: kind === 'greedy' });
}

/** A random valid loadout: `size` abilities from the pool, then up to the upgrade limit among those whose requirements are met. */
export function randomLoadout(c: Content, seed: number, size = c.rules.loadout.abilities): Loadout {
  const rng = new Rng(seed ^ 0x5eed);
  const abilities = rng.shuffle([...c.abilityOrder]).slice(0, Math.min(size, c.abilityOrder.length));
  abilities.sort((a, b) => c.abilityOrder.indexOf(a) - c.abilityOrder.indexOf(b));
  const eligible = c.upgradeOrder.filter((u) => upgradeAllowed(c.upgrades[u]!.requires, abilities));
  const upgrades = rng.shuffle([...eligible]).slice(0, c.rules.loadout.upgrades);
  upgrades.sort((a, b) => c.upgradeOrder.indexOf(a) - c.upgradeOrder.indexOf(b));
  return { abilities, upgrades };
}

function loadoutFor(c: Content, spec: BatchSpec, seed: number): { loadout: Loadout; preset: string | null } {
  const l = spec.loadout;
  if ('preset' in l) {
    const p = c.presets.find((x) => x.id === l.preset);
    if (!p) throw new Error(`Unknown preset "${l.preset}"`);
    return { loadout: { abilities: p.abilities, upgrades: p.upgrades }, preset: p.id };
  }
  if ('custom' in l) return { loadout: l.custom, preset: null };
  return { loadout: randomLoadout(c, seed, l.size), preset: null };
}

/** Runs a batch on the current thread. Rows come back in seed order; the same spec always gives the same rows. */
export function runBatch(spec: BatchSpec, c: Content = contentFor(spec.overrides)): FightRow[] {
  const rows: FightRow[] = [];
  for (const seed of spec.seeds) {
    const { loadout, preset } = loadoutFor(c, spec, seed);
    const bot = makeBot(spec.bot, seed);
    try {
      const r = runFight(c, loadout, bot, { settings: spec.settings, meta: { preset: preset ?? undefined } });
      rows.push(rowOf(seed, bot.name, preset, r.summary, r.commands, spec.keepRecords ? r.record : undefined));
    } catch (e) {
      const msg = e instanceof Error ? e.message : String(e);
      rows.push({
        ...emptyRow(seed, bot.name, preset, loadout),
        outcome: 'error',
        error: msg,
        record: e instanceof BotError ? e.record : undefined,
      });
    }
  }
  return rows;
}

function emptyRow(seed: number, bot: string, preset: string | null, l: Loadout): FightRow {
  const z = { player: 0, minion: 0, enemy: 0, hazard: 0 };
  return {
    seed,
    bot,
    preset,
    abilities: l.abilities,
    upgrades: l.upgrades,
    outcome: 'unfinished',
    rounds: 0,
    cause: null,
    deathCategory: null,
    deathSource: null,
    playerDamage: { ...z },
    friendlyFireShare: 0,
    enemyDamage: { ...z },
    hazardFires: 0,
    hazardFiresHitPlayer: 0,
    peakMinions: 0,
    wavesSpawned: 0,
    wardenReachedPlayerRound: null,
    wardenDiedRound: null,
    kills: 0,
    commands: 0,
    abilitiesUsed: {},
  };
}

function rowOf(seed: number, bot: string, preset: string | null, s: FightSummary, commands: number, record?: FightRecord): FightRow {
  return {
    seed,
    bot,
    preset,
    abilities: s.loadout.abilities,
    upgrades: s.loadout.upgrades,
    outcome: s.outcome,
    rounds: s.rounds,
    cause: s.cause,
    deathCategory: s.causeOfDeath?.category ?? null,
    deathSource: s.causeOfDeath ? `${s.causeOfDeath.source}/${s.causeOfDeath.via}` : null,
    playerDamage: s.playerDamageTaken,
    friendlyFireShare: s.friendlyFireShare,
    enemyDamage: s.enemyDamageTaken,
    hazardFires: s.hazardFires.length,
    hazardFiresHitPlayer: s.hazardFires.filter((h) => h.victims.includes('player')).length,
    peakMinions: s.peakMinions,
    wavesSpawned: s.wavesSpawned,
    wardenReachedPlayerRound: s.wardenReachedPlayerRound,
    wardenDiedRound: s.wardenDiedRound,
    kills: Object.values(s.kills).reduce((a, b) => a + b, 0),
    commands,
    abilitiesUsed: s.abilitiesUsed,
    record,
  };
}

export function seedRange(from: number, count: number): number[] {
  return Array.from({ length: count }, (_, i) => from + i);
}
