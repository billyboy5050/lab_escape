import { cheb, samePos } from '../state/grid';
import type { Pos, Unit } from '../state/types';
import { FAR, meleeDistance, minBy, pathTo, type Reach } from '../rules/pathing';
import type { World } from '../rules/world';

/** The standard tie-break: nearest, then lowest current HP, then lowest unit ID. */
export function pickNearest(cands: readonly Unit[], dist: (u: Unit) => number): Unit | undefined {
  return minBy(cands, (u) => [dist(u), u.hp, u.id]);
}

/** Nearest by range distance, for ranged and flying units. */
export function nearestRanged(from: Pos, cands: readonly Unit[]): Unit | undefined {
  return pickNearest(cands, (u) => cheb(from, u.pos));
}

/** Nearest by path length to an adjacent tile, for ground melee units. Falls back to range distance if none is reachable. */
export function nearestMelee(w: World, from: Pos, cands: readonly Unit[]): Unit | undefined {
  const scored = cands.map((u) => ({ u, d: meleeDistance(w, from, u.pos) }));
  const reachableOnes = scored.filter((x) => x.d < FAR);
  if (reachableOnes.length) return minBy(reachableOnes, (x) => [x.d, x.u.hp, x.u.id])?.u;
  return nearestRanged(from, cands);
}

/** Walks the unit to a destination from a reach map. Returns false if it died on the way. */
export function moveTo(w: World, u: Unit, reach: Reach, dest: Pos): boolean {
  if (samePos(dest, u.pos)) return !u.dead;
  return w.moveAlong(u, pathTo(w, reach, dest), u.flying ? 'fly' : 'walk');
}

export function stepsTo(w: World, reach: Reach, p: Pos): number {
  return reach.steps[w.b.idx(p)]!;
}
