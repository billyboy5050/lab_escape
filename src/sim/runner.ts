import type { Content } from '../content/types';
import { newGame, step } from '../engine/step';
import type { FightSettings, GameState, Loadout } from '../state/types';
import { newRecord, type FightRecord } from '../telemetry/record';
import { SummaryBuilder, type FightSummary } from '../telemetry/summary';
import type { Bot } from './bots';

export interface FightResult {
  seed: number | null;
  bot: string;
  record: FightRecord;
  summary: FightSummary;
  state: GameState;
  commands: number;
}

export class BotError extends Error {
  constructor(
    message: string,
    readonly record: FightRecord,
  ) {
    super(message);
    this.name = 'BotError';
  }
}

/**
 * Plays one fight headlessly. A bot that issues an illegal command, or a fight that exceeds the command
 * limit (a likely infinite loop), raises a BotError carrying the command log so far, for a bug report.
 */
export function runFight(c: Content, loadout: Loadout, bot: Bot, opts: { settings?: FightSettings; maxCommands?: number; meta?: FightRecord['meta'] } = {}): FightResult {
  const settings = opts.settings ?? {};
  const g = newGame(c, loadout, settings);
  const record = newRecord(c, loadout, settings, { bot: bot.name, seed: bot.seed, ...opts.meta });
  const summary = new SummaryBuilder(loadout);
  summary.add(g.events);
  let s = g.state;
  const max = opts.maxCommands ?? 2000;
  let n = 0;
  while (!s.outcome) {
    if (++n > max) throw new BotError(`Command limit (${max}) reached in round ${s.round}: possible infinite loop`, record);
    const cmd = bot.decide(c, s);
    if (!cmd) break;
    const r = step(c, s, cmd);
    if (!r.ok) throw new BotError(`Bot ${bot.name} issued an illegal command in round ${s.round}: ${r.error} (${JSON.stringify(cmd)})`, record);
    record.commands.push(cmd);
    summary.add(r.events);
    s = r.state;
  }
  return { seed: bot.seed ?? null, bot: bot.name, record, summary: summary.finish(s), state: s, commands: record.commands.length };
}
