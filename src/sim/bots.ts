import type { Content } from '../content/types';
import { grappleSelfPath } from '../effects/abilities';
import { classify, hazardDamageToPlayer, hazardTargets } from '../ai/hazards';
import { createWorld } from '../engine/phases';
import { actionStatuses, commandFor, legalCommands, moveCommand, moveOptions } from '../engine/commands';
import { step } from '../engine/step';
import { simulateActors } from '../preview/preview';
import { boardFor, cheb } from '../state/grid';
import type { Command, GameState, Unit } from '../state/types';
import { Rng } from './rng';

export interface Bot {
  readonly name: string;
  readonly seed?: number;
  decide(c: Content, s: GameState): Command | null;
}

/** Random legal bot: finds crashes, infinite loops and exploits. Draws from a seeded generator. */
export class RandomBot implements Bot {
  readonly name = 'random';
  private rng: Rng;
  constructor(readonly seed: number) {
    this.rng = new Rng(seed);
  }
  decide(c: Content, s: GameState): Command {
    return this.rng.pick(legalCommands(c, s));
  }
}

/** Replays a fixed command script (used for scripted regression fights and golden replays). */
export class ScriptedBot implements Bot {
  private i = 0;
  constructor(
    private readonly script: readonly Command[],
    readonly name = 'scripted',
  ) {}
  decide(): Command | null {
    return this.i < this.script.length ? this.script[this.i++]! : null;
  }
}

/**
 * Greedy bot: each action maximises its immediate preview score. A candidate is scored by applying it,
 * then playing out this round's minion and enemy phases (hazards stay hidden), and valuing the result.
 * One action in ten is a random legal action from the seeded generator, so a batch is a spread of fights.
 */
export interface GreedyOptions {
  /** Random legal actions per ten decisions (the spec's one in ten). */
  randomPerTen?: number;
  /**
   * Treat a hazard showing its cue as a threat to the tiles it covers, as a player who has learned the
   * room would. Off, the bot sees only what previews and intents show.
   */
  heedCues?: boolean;
}

export class GreedyBot implements Bot {
  readonly name: string;
  private rng: Rng;
  private readonly opts: Required<GreedyOptions>;
  constructor(
    readonly seed: number,
    opts: GreedyOptions = {},
  ) {
    this.rng = new Rng(seed);
    this.opts = { randomPerTen: opts.randomPerTen ?? 1, heedCues: opts.heedCues ?? true };
    this.name = this.opts.heedCues ? 'greedy' : 'greedy-naive';
  }

  decide(c: Content, s: GameState): Command {
    if (this.opts.randomPerTen > 0 && this.rng.chance(this.opts.randomPerTen, 10)) return this.rng.pick(legalCommands(c, s));
    return greedyChoice(c, s, this.opts.heedCues).cmd;
  }
}

export function greedyChoice(c: Content, s: GameState, heedCues = true): { cmd: Command; score: number } {
  let best: Command = { type: 'endTurn' };
  let bestScore = scoreAfterRound(c, s, heedCues);
  for (const cmd of candidateCommands(c, s)) {
    const score = scoreCommand(c, s, cmd, heedCues);
    if (score > bestScore) {
      best = cmd;
      bestScore = score;
    }
  }
  return { cmd: best, score: bestScore };
}

function scoreCommand(c: Content, s: GameState, cmd: Command, heedCues: boolean): number {
  const r = step(c, s, cmd);
  if (!r.ok) return -Infinity;
  if (cmd.type === 'sprint' && !r.state.outcome) {
    // Sprint only pays off through the move it allows: score it as its best follow-up move.
    let best = -Infinity;
    for (const m of moveOptions(c, r.state)) {
      const r2 = step(c, r.state, moveCommand(m.path));
      if (r2.ok) best = Math.max(best, scoreAfterRound(c, r2.state, heedCues));
    }
    return best;
  }
  return scoreAfterRound(c, r.state, heedCues);
}

/** Candidate commands, pruned to keep the bot fast: duplicate grapple landings and far shield pairs are dropped. */
export function candidateCommands(c: Content, s: GameState): Command[] {
  const out: Command[] = [];
  const p = s.units.find((u) => u.id === 0);
  if (!p || s.phase !== 'player' || s.outcome) return out;
  for (const m of moveOptions(c, s)) out.push(moveCommand(m.path));
  const enemies = s.units.filter((u) => u.team === 'enemy' && !u.dead);
  for (const st of actionStatuses(c, s)) {
    if (!st.usable) continue;
    if (st.id === 'sprint') {
      if (s.movement === 0 || moveOptions(c, s).length === 0) out.push({ type: 'sprint' });
      continue;
    }
    let opts = st.options;
    if (st.id === 'barrier_shield') opts = opts.filter((o) => cheb(o.target, p.pos) <= 2 && o.target2 && cheb(o.target2, p.pos) <= 2);
    if (st.id === 'spore_pod') opts = opts.filter((o) => enemies.some((e) => cheb(e.pos, o.target) <= 1));
    if (st.id === 'grapple_hook') {
      // Many self-pull targets land on the same tile: keep one option per landing tile.
      const w = createWorld(c, s);
      const seen = new Set<string>();
      opts = opts.filter((o) => {
        if (o.mode !== 'self') return true;
        const walk = grappleSelfPath(w, p, o.target);
        const land = walk[walk.length - 1]!;
        const key = `${land.x},${land.y}`;
        if (seen.has(key)) return false;
        seen.add(key);
        return true;
      });
    }
    for (const o of opts) out.push(commandFor(st, o));
  }
  return out;
}

/** Plays out this round's minion and enemy phases from the state and values what is left. */
export function scoreAfterRound(c: Content, s: GameState, heedCues = true): number {
  if (s.outcome) return terminal(s);
  const after = simulateActors(c, s).state;
  let v = scoreState(c, after);
  if (heedCues && !after.outcome) {
    const threat = cueThreat(c, after);
    const p = after.units.find((u) => u.id === 0);
    if (p && threat >= p.hp) v -= 500_000;
    else v -= threat * 100;
  }
  return v;
}

/** Damage the player would take from hazards showing their cue, if they fired on the current positions. */
export function cueThreat(c: Content, s: GameState): number {
  const primed = s.hazards.filter((h) => h.state === 'primed');
  if (!primed.length) return 0;
  const w = createWorld(c, s);
  let dmg = 0;
  for (const h of primed) {
    const cls = classify(hazardTargets(w, h));
    if (cls.player && !cls.elite) dmg += hazardDamageToPlayer(w, h);
  }
  return dmg;
}

function terminal(s: GameState): number {
  return s.outcome?.result === 'win' ? 1_000_000 - s.round : -1_000_000 + s.round;
}

/** Integer valuation of a state: the player's health first, then enemy health and future damage, then assets. */
export function scoreState(c: Content, s: GameState): number {
  if (s.outcome) return terminal(s);
  const p = s.units.find((u) => u.id === 0);
  if (!p) return -1_000_000;
  const rs = c.rules.statuses;
  let v = p.hp * 100;
  if (p.statuses.poison) v -= p.statuses.poison.remaining * rs.poison.damage * 90;
  if (p.statuses.parasite) v -= p.statuses.parasite.remaining * rs.parasite.damage * 90;
  const board = boardFor(c.map);
  for (const u of s.units) {
    if (u.dead) continue;
    if (u.team === 'enemy') {
      v -= 24 * u.hp + 80;
      if (u.def === 'medic') v -= 60;
      if (u.statuses.poison) v += 12 * Math.min(u.hp, u.statuses.poison.remaining * rs.poison.damage);
      if (u.statuses.parasite) v += 14 * Math.min(u.hp, u.statuses.parasite.remaining * rs.parasite.damage) + 20;
      const armor = u.armor - (u.statuses.corrode?.stacks ?? 0);
      v -= 30 * Math.max(0, armor);
      // Offensive potential: enemies the player can see within range 5.
      if (cheb(u.pos, p.pos) <= 5 && board.hasLOS(u.pos, p.pos)) v += 6;
    } else if (u.kind === 'minion') {
      v += 12 * u.hp + 30 + 6 * (u.ammo ?? 0) + 10 * (u.charges ?? 0);
      if (u.statuses.poison) v -= 8 * u.statuses.poison.remaining;
    }
  }
  for (const o of s.objects) {
    if (o.kind === 'egg') v += 40 * (o.host === 'floor' ? c.rules.eggs.floorHatchlings : 1) + 4 * (o.hp ?? 0);
    else if (o.kind === 'mine') v += 10;
    else if (o.kind === 'shield') v += 3 * (o.hp ?? 0);
  }
  return v;
}

export function playerOf(s: GameState): Unit | undefined {
  return s.units.find((u) => u.id === 0);
}
