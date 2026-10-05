import type { Content } from '../content/types';
import type { GameEvent } from '../state/types';
import { hashJson } from '../util/hash';
import { checkCompatible, eventsToJsonl, replay, type FightRecord } from '../telemetry/record';
import { GreedyBot } from './bots';
import { runFight } from './runner';

/**
 * A golden replay: a fixed command script for a preset and the event log it must produce. If a build
 * changes the event log without an intended rule change, the golden test fails. Content changes make
 * the content hash differ, and the goldens are re-recorded on purpose (npm run golden:record).
 */
export interface GoldenFile {
  preset: string;
  note: string;
  record: FightRecord;
  eventCount: number;
  eventsHash: string;
  finalStateHash: string;
  outcome: string;
  rounds: number;
}

export const GOLDEN_SEED = 1;

/**
 * The scripted fight for a preset. The hand simulations behind the design document were summaries,
 * not move-by-move logs, so the script is a greedy-bot fight frozen into a command list; replace it
 * with a transcribed hand line when one exists.
 */
export function recordGolden(c: Content, presetId: string): { golden: GoldenFile; events: GameEvent[] } {
  const p = c.presets.find((x) => x.id === presetId);
  if (!p) throw new Error(`Unknown preset ${presetId}`);
  const fight = runFight(c, { abilities: p.abilities, upgrades: p.upgrades }, new GreedyBot(GOLDEN_SEED), { meta: { preset: p.id, label: `golden ${p.id}` } });
  delete fight.record.meta?.created;
  const rep = replay(c, fight.record);
  return {
    golden: {
      preset: p.id,
      note: 'Scripted regression fight. Re-record on purpose when content values or rules change: npm run golden:record',
      record: fight.record,
      eventCount: rep.events.length,
      eventsHash: hashJson(rep.events),
      finalStateHash: hashJson(rep.state),
      outcome: fight.summary.outcome,
      rounds: fight.summary.rounds,
    },
    events: rep.events,
  };
}

export interface GoldenCheck {
  preset: string;
  ok: boolean;
  problem?: string;
  firstDifference?: number;
}

export function verifyGolden(c: Content, g: GoldenFile, expectedEvents?: GameEvent[]): GoldenCheck {
  try {
    checkCompatible(c, g.record);
  } catch (e) {
    return { preset: g.preset, ok: false, problem: `${(e as Error).message}. Re-record with: npm run golden:record` };
  }
  let rep;
  try {
    rep = replay(c, g.record);
  } catch (e) {
    return { preset: g.preset, ok: false, problem: (e as Error).message };
  }
  const eventsHash = hashJson(rep.events);
  if (eventsHash === g.eventsHash && hashJson(rep.state) === g.finalStateHash) return { preset: g.preset, ok: true };
  let firstDifference: number | undefined;
  if (expectedEvents) {
    const n = Math.max(expectedEvents.length, rep.events.length);
    for (let i = 0; i < n; i++) {
      if (JSON.stringify(expectedEvents[i]) !== JSON.stringify(rep.events[i])) {
        firstDifference = i;
        break;
      }
    }
  }
  return {
    preset: g.preset,
    ok: false,
    problem: `Event log changed (${rep.events.length} events, expected ${g.eventCount})${firstDifference !== undefined ? `; first difference at event ${firstDifference}` : ''}`,
    firstDifference,
  };
}

export { eventsToJsonl };
