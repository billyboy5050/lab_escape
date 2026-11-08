import { buildContent } from '../content/load';
import type { Content, ContentFiles, RoomFile } from '../content/types';
import type { FightSettings, GameEvent } from '../state/types';
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

/** The preset that fights the golden rooms: it uses all three kits. */
export const GOLDEN_ROOM_PRESET = 'hybrid';

/** One golden replay to record or check: each file in golden/ is named after `name`. */
export interface GoldenFight {
  name: string;
  content: Content;
  preset: string;
  settings: FightSettings;
}

/**
 * Every golden fight: each preset in the Armory, then the golden room preset in each room of golden/rooms.json. The rooms
 * are not game content. They exist to pin down fights of other sizes, so they join the content only for their own fights.
 */
export function goldenFights(files: ContentFiles, rooms: RoomFile[]): GoldenFight[] {
  const armory = buildContent(files);
  const withRooms = buildContent({ ...files, rooms });
  return [
    ...armory.presets.map((p) => ({ name: p.id, content: armory, preset: p.id, settings: {} })),
    ...rooms.map((r) => ({ name: `room-${r.id}`, content: withRooms, preset: GOLDEN_ROOM_PRESET, settings: { room: r.id } })),
  ];
}

/**
 * The scripted fight for a preset. The hand simulations behind the design document were summaries,
 * not move-by-move logs, so the script is a greedy-bot fight frozen into a command list (spec,
 * Headless simulation).
 */
export function recordGolden(c: Content, presetId: string, settings: FightSettings = {}): { golden: GoldenFile; events: GameEvent[] } {
  const p = c.presets.find((x) => x.id === presetId);
  if (!p) throw new Error(`Unknown preset ${presetId}`);
  const fight = runFight(c, { abilities: p.abilities, upgrades: p.upgrades }, new GreedyBot(GOLDEN_SEED), { settings, meta: { preset: p.id, label: settings.room ? `golden ${p.id} in ${settings.room}` : `golden ${p.id}` } });
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
