import { DIRS4, addPos, cheb } from '../state/grid';
import type { Pos, Unit } from '../state/types';
import type { World } from './world';

export const FAR = 9999;

export interface Reach {
  /** Steps to each tile index, -1 where unreachable. */
  steps: Int16Array;
  prev: Int16Array;
  /** Tiles the unit could end on, including its own tile, sorted by steps then row-major. */
  dests: Pos[];
}

/**
 * Breadth-first search over 4-direction steps (north, east, south, west, in that order).
 * Ground units cannot pass through units; flying units can pass over units but not end on them.
 * Nobody passes through walls, pillars, the shutter, the wall gun, shield segments or eggs.
 */
export function reachable(w: World, u: Unit, maxSteps: number): Reach {
  const b = w.b;
  const n = b.width * b.height;
  const steps = new Int16Array(n).fill(-1);
  const prev = new Int16Array(n).fill(-1);
  const start = b.idx(u.pos);
  steps[start] = 0;
  const queue = [u.pos];
  const occupied = new Uint8Array(n);
  for (const o of w.activeUnits()) if (o.id !== u.id) occupied[b.idx(o.pos)] = 1;
  const blocked = new Uint8Array(n);
  for (const o of w.s.objects) if (!o.destroyed && (o.kind === 'shield' || o.kind === 'egg')) blocked[b.idx(o.pos)] = 1;
  for (let qi = 0; qi < queue.length; qi++) {
    const p = queue[qi]!;
    const sp = steps[b.idx(p)]!;
    if (sp >= maxSteps) continue;
    for (const d of DIRS4) {
      const q = addPos(p, d);
      if (b.blocksMove(q)) continue;
      const qi2 = b.idx(q);
      if (steps[qi2] !== -1 || blocked[qi2]) continue;
      if (!u.flying && occupied[qi2]) continue;
      steps[qi2] = sp + 1;
      prev[qi2] = b.idx(p);
      queue.push(q);
    }
  }
  const dests = queue.filter((p) => !occupied[b.idx(p)]);
  dests.sort((a, c) => steps[b.idx(a)]! - steps[b.idx(c)]! || a.y - c.y || a.x - c.x);
  return { steps, prev, dests };
}

/** The path from the unit's tile to dest, excluding the start tile. */
export function pathTo(w: World, reach: Reach, dest: Pos): Pos[] {
  const b = w.b;
  const out: Pos[] = [];
  let i = b.idx(dest);
  if (reach.steps[i] === -1) throw new Error('pathTo: destination not reachable');
  while (reach.steps[i]! > 0) {
    out.push(b.pos(i));
    i = reach.prev[i]!;
  }
  return out.reverse();
}

/**
 * Ground path distance from every tile to the nearest goal tile, ignoring units (they move) but
 * respecting terrain, shield segments and eggs. Unreachable tiles hold FAR.
 */
export function distanceField(w: World, goals: Pos[]): Int16Array {
  const b = w.b;
  const n = b.width * b.height;
  const dist = new Int16Array(n).fill(FAR);
  const blocked = new Uint8Array(n);
  for (const o of w.s.objects) if (!o.destroyed && (o.kind === 'shield' || o.kind === 'egg')) blocked[b.idx(o.pos)] = 1;
  const queue: Pos[] = [];
  for (const g of goals) {
    const gi = b.idx(g);
    if (dist[gi] === FAR) {
      dist[gi] = 0;
      queue.push(g);
    }
  }
  for (let qi = 0; qi < queue.length; qi++) {
    const p = queue[qi]!;
    const dp = dist[b.idx(p)]!;
    for (const d of DIRS4) {
      const q = addPos(p, d);
      if (b.blocksMove(q)) continue;
      const i = b.idx(q);
      if (blocked[i] || dist[i] !== FAR) continue;
      dist[i] = dp + 1;
      queue.push(q);
    }
  }
  return dist;
}

/** Tiles from which a melee unit can hit the target: its 8 neighbours that a ground unit could stand on. */
export function adjacentGoals(w: World, target: Pos): Pos[] {
  return w.b.neighbors8(target).filter((p) => !w.b.blocksMove(p) && !w.blockingObjectAt(p));
}

/** Path length (ignoring units) for a ground unit at `from` to get adjacent to `target`. */
export function meleeDistance(w: World, from: Pos, target: Pos): number {
  if (cheb(from, target) <= 1) return 0;
  return distanceField(w, adjacentGoals(w, target))[w.b.idx(from)]!;
}

/**
 * The reachable destination that gets closest to a target: by path distance to an adjacent tile
 * (ignoring units), then range distance, then fewest steps, then lowest row, then lowest column.
 */
export function approachDest(w: World, reach: Reach, target: Pos): Pos {
  const field = distanceField(w, adjacentGoals(w, target));
  let best: Pos | null = null;
  let bestKey: number[] = [];
  for (const d of reach.dests) {
    const i = w.b.idx(d);
    const key = [field[i]!, cheb(d, target), reach.steps[i]!, d.y, d.x];
    if (!best || lexLess(key, bestKey)) {
      best = d;
      bestKey = key;
    }
  }
  return best!;
}

export function lexLess(a: readonly number[], b: readonly number[]): boolean {
  for (let i = 0; i < a.length; i++) {
    if (a[i]! < b[i]!) return true;
    if (a[i]! > b[i]!) return false;
  }
  return false;
}

/** Picks the item with the smallest key (lexicographic). Ties keep the earliest item. */
export function minBy<T>(items: readonly T[], key: (t: T) => readonly number[]): T | undefined {
  let best: T | undefined;
  let bestKey: readonly number[] = [];
  for (const it of items) {
    const k = key(it);
    if (best === undefined || lexLess(k, bestKey)) {
      best = it;
      bestKey = k;
    }
  }
  return best;
}
