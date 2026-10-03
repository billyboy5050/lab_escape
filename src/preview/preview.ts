import type { Content } from '../content/types';
import { step } from '../engine/step';
import { createWorld, runEnemyPhase, runMinionPhase } from '../engine/phases';
import { FightOver } from '../rules/world';
import { cloneState } from '../state/state';
import type { Command, EntityRef, Frame, GameEvent, GameObject, GameState, Outcome, Pos, StatusId, Team, Unit } from '../state/types';

export interface HitPreview {
  ref: EntityRef;
  def: string;
  side: Team;
  pos: Pos;
  /** Damage after armor, summed over every hit in the action. */
  damage: number;
  absorbed: number;
  killed: boolean;
  statuses: StatusId[];
  healed: number;
  /** A player-side unit or object harmed by the player's own side. */
  friendly: boolean;
}

export interface LinePreview {
  from: Pos;
  to: Pos;
  kind: 'shot' | 'chain' | 'reflect' | 'charge' | 'melee' | 'heal';
}

export interface EffectSummary {
  hits: HitPreview[];
  areaTiles: Pos[];
  lines: LinePreview[];
  moves: { id: number; from: Pos; to: Pos; path: Pos[] }[];
  placed: GameObject[];
  spawned: Unit[];
  minesTriggered: Pos[];
  friendlyFire: boolean;
  playerHarmed: boolean;
}

export interface CommandPreview extends EffectSummary {
  ok: boolean;
  error?: string;
  state?: GameState;
  events: GameEvent[];
  outcome?: Outcome | null;
  /** Enemy and minion intents recomputed after the action, when requested. */
  intents?: IntentReport;
}

const HARMFUL: readonly StatusId[] = ['poison', 'parasite', 'corrode', 'pinned', 'slowed'];

/** Summarises a list of events into what the UI draws: hits, areas, lines, moves and friendly fire. */
export function summarizeEffects(events: GameEvent[], before: GameState): EffectSummary {
  const hits = new Map<string, HitPreview>();
  const areaTiles: Pos[] = [];
  const lines: LinePreview[] = [];
  const moves = new Map<number, { id: number; from: Pos; to: Pos; path: Pos[] }>();
  const placed: GameObject[] = [];
  const spawned: Unit[] = [];
  const minesTriggered: Pos[] = [];
  const unitInfo = new Map<number, { def: string; team: Team; pos: Pos }>();
  for (const u of before.units) unitInfo.set(u.id, { def: u.def, team: u.team, pos: u.pos });
  const hit = (ref: EntityRef, def: string, side: Team, pos: Pos): HitPreview => {
    const key = `${ref.kind}${ref.id}`;
    let h = hits.get(key);
    if (!h) {
      h = { ref, def, side, pos: { ...pos }, damage: 0, absorbed: 0, killed: false, statuses: [], healed: 0, friendly: false };
      hits.set(key, h);
    }
    return h;
  };
  for (const e of events) {
    switch (e.t) {
      case 'DamageDealt': {
        const h = hit(e.target, e.targetDef, e.targetSide, e.pos);
        h.damage += e.amount;
        h.absorbed += e.absorbed;
        if (e.killed) h.killed = true;
        if (e.targetSide === 'player' && e.source.side === 'player') h.friendly = true;
        break;
      }
      case 'StatusApplied': {
        const info = unitInfo.get(e.target);
        if (!info) break;
        const h = hit({ kind: 'unit', id: e.target }, info.def, info.team, info.pos);
        if (!h.statuses.includes(e.status)) h.statuses.push(e.status);
        if (info.team === 'player' && e.source.side === 'player' && HARMFUL.includes(e.status)) h.friendly = true;
        break;
      }
      case 'Healed': {
        const info = unitInfo.get(e.target);
        if (info) hit({ kind: 'unit', id: e.target }, info.def, info.team, info.pos).healed += e.amount;
        break;
      }
      case 'AreaEffect':
        for (const t of e.tiles) if (!areaTiles.some((a) => a.x === t.x && a.y === t.y)) areaTiles.push(t);
        break;
      case 'ProjectileFired':
        if (e.path.length) lines.push({ from: e.from, to: e.path[e.path.length - 1]!, kind: e.reflected ? 'reflect' : 'shot' });
        break;
      case 'ChainArc':
        lines.push({ from: e.from, to: e.to, kind: 'chain' });
        break;
      case 'Charged':
        lines.push({ from: e.from, to: e.to, kind: 'charge' });
        break;
      case 'UnitMoved': {
        const m = moves.get(e.id);
        if (m) {
          m.to = e.to;
          m.path.push(...e.path);
        } else moves.set(e.id, { id: e.id, from: e.from, to: e.to, path: [...e.path] });
        const info = unitInfo.get(e.id);
        if (info) info.pos = e.to;
        break;
      }
      case 'UnitSpawned':
        spawned.push(e.unit);
        unitInfo.set(e.unit.id, { def: e.unit.def, team: e.unit.team, pos: e.unit.pos });
        break;
      case 'ObjectPlaced':
        placed.push(e.object);
        break;
      case 'ObjectTriggered':
        minesTriggered.push(e.pos);
        break;
    }
  }
  const list = [...hits.values()];
  return {
    hits: list,
    areaTiles,
    lines,
    moves: [...moves.values()],
    placed,
    spawned,
    minesTriggered,
    friendlyFire: list.some((h) => h.friendly),
    playerHarmed: list.some((h) => h.ref.kind === 'unit' && h.ref.id === 0 && (h.damage > 0 || h.statuses.some((s) => HARMFUL.includes(s)))),
  };
}

/** Dry-runs a player command on a cloned state with the same engine code that resolves it. */
export function previewCommand(c: Content, s: GameState, cmd: Command, opts: { intents?: boolean } = {}): CommandPreview {
  const r = step(c, s, cmd);
  if (!r.ok) return { ok: false, error: r.error, events: [], ...summarizeEffects([], s) };
  const summary = summarizeEffects(r.events, s);
  const out: CommandPreview = { ok: true, state: r.state, events: r.events, outcome: r.state.outcome, ...summary };
  if (opts.intents && !r.state.outcome && r.state.phase === 'player') out.intents = computeIntents(c, r.state);
  return out;
}

/**
 * Runs the minion and enemy phases on a copy of the state, without frames: the cheap form of intents,
 * used by bots to score an action by what follows it this round.
 */
export function simulateActors(c: Content, s: GameState): { state: GameState; events: GameEvent[] } {
  const sim = cloneState(s);
  const w = createWorld(c, sim);
  if (s.phase === 'player' && !s.outcome) {
    try {
      runMinionPhase(w);
      runEnemyPhase(w);
    } catch (e) {
      if (!(e instanceof FightOver)) throw e;
    }
  }
  return { state: sim, events: w.events };
}

// ------------------------------------------------------------------------------------------ intents

export interface Intent {
  id: number;
  def: string;
  team: Team;
  /** In its arrival round the unit only moves; its destination is shown, not an attack. */
  arrival: boolean;
  from: Pos;
  to: Pos;
  path: Pos[];
  lines: LinePreview[];
  tiles: Pos[];
  hits: HitPreview[];
  /** The unit dies before its turn comes (killed by someone acting earlier). */
  diesFirst: boolean;
  /** Docked drones and units with nothing to do. */
  idle: boolean;
  label: string;
}

export interface IntentReport {
  intents: Intent[];
  /** Damage the player takes from all minion and enemy actions this round, if the turn ended now. */
  playerDamage: number;
  playerDies: boolean;
  outcome: Outcome | null;
  frames: Frame[];
}

/**
 * What every minion and enemy will do if the player ends the turn now: the minion and enemy phases are
 * run on a cloned state, so each intent already accounts for the units that act before it. The
 * environment phase (hazards, statuses, eggs) is not run, so hazards stay hidden.
 */
export function computeIntents(c: Content, s: GameState): IntentReport {
  const empty: IntentReport = { intents: [], playerDamage: 0, playerDies: false, outcome: null, frames: [] };
  if (s.phase !== 'player' || s.outcome) return empty;
  const sim = cloneState(s);
  const w = createWorld(c, sim, { frames: true });
  const actors = s.units.filter((u) => !u.dead && u.kind !== 'player').map((u) => u.id);
  let outcome: Outcome | null = null;
  try {
    runMinionPhase(w);
    runEnemyPhase(w);
  } catch (e) {
    if (!(e instanceof FightOver)) throw e;
    outcome = e.outcome;
  }
  w.closeFrame('tail', null);
  const frames = w.frames ?? [];
  const intents: Intent[] = [];
  let playerDamage = 0;
  for (const id of actors) {
    const u = s.units.find((x) => x.id === id)!;
    const frame = frames.find((f) => f.actor === id);
    const docked = u.droneState === 'docked';
    if (!frame) {
      intents.push({
        id,
        def: u.def,
        team: u.team,
        arrival: u.arrivalRound === s.round,
        from: u.pos,
        to: u.pos,
        path: [],
        lines: [],
        tiles: [],
        hits: [],
        diesFirst: !docked && !outcome,
        idle: true,
        label: docked ? 'Docked' : outcome ? 'Does not act (the fight ends first)' : 'Dies before acting',
      });
      continue;
    }
    const prevState = frames[frames.indexOf(frame) - 1]?.state ?? s;
    const sum = summarizeEffects(frame.events, prevState);
    const mv = sum.moves.find((m) => m.id === id);
    const hits = sum.hits;
    for (const h of hits) if (h.ref.kind === 'unit' && h.ref.id === 0) playerDamage += h.damage;
    const arrival = u.arrivalRound === s.round;
    intents.push({
      id,
      def: u.def,
      team: u.team,
      arrival,
      from: u.pos,
      to: mv ? mv.to : u.pos,
      path: mv ? mv.path : [],
      lines: sum.lines,
      tiles: sum.areaTiles,
      hits,
      diesFirst: false,
      idle: !mv && !hits.length && !sum.lines.length,
      label: describeIntent(mv, hits, frame.events, arrival),
    });
  }
  return { intents, playerDamage, playerDies: outcome?.result === 'lose', outcome, frames };
}

function describeIntent(mv: { to: Pos } | undefined, hits: HitPreview[], events: GameEvent[], arrival: boolean): string {
  const name = (def: string, id: number) => (id === 0 ? 'you' : `${def} ${id}`);
  const parts: string[] = [];
  if (mv) parts.push(`moves to ${String.fromCharCode(65 + mv.to.x)}${mv.to.y + 1}`);
  if (arrival) return parts.length ? `Arriving: ${parts[0]}` : 'Arriving: holds position';
  if (events.some((e) => e.t === 'Purged')) parts.push('purges its poison');
  if (events.some((e) => e.t === 'DroneDocked')) parts.push('docks');
  const dmg = hits.filter((h) => h.damage > 0 || h.killed);
  for (const h of dmg.slice(0, 3)) parts.push(`hits ${h.ref.kind === 'unit' ? name(h.def, h.ref.id) : h.def} for ${h.damage}${h.killed ? ' (kills)' : ''}`);
  if (dmg.length > 3) parts.push(`and ${dmg.length - 3} more`);
  const heals = hits.filter((h) => h.healed > 0);
  for (const h of heals) parts.push(`heals ${name(h.def, h.ref.id as number)} for ${h.healed}`);
  if (!parts.length) return 'Holds';
  const s = parts.join(', ');
  return s.charAt(0).toUpperCase() + s.slice(1);
}
