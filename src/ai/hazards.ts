import { addPos, samePos } from '../state/grid';
import type { EntityRef, GameObject, Hazard, HazardDecision, Pos, SourceRef, Unit } from '../state/types';
import { fireProjectile } from '../rules/projectile';
import { intercomTrigger } from '../rules/intercom';
import type { World } from '../rules/world';

export interface HazardTargets {
  /** Units in the area, or for the wall gun the first unit in its lane. */
  units: Unit[];
  /** Wall gun only: the first blocking object in its lane, when it comes before any unit. */
  object?: GameObject;
  /** Tiles the hazard covers (for the gun, the lane up to what it hits). */
  tiles: Pos[];
}

export interface Classified {
  elite: boolean;
  player: boolean;
  soldier: boolean;
  playerSide: boolean;
  victims: EntityRef[];
}

/** The full lane of a lane hazard, from the tile after the gun to the edge of the map. */
export function laneTiles(w: World, hz: Hazard): Pos[] {
  const out: Pos[] = [];
  let p = hz.tiles[0]!;
  for (;;) {
    p = addPos(p, hz.dir!);
    if (!w.b.inBounds(p)) break;
    out.push(p);
  }
  return out;
}

/** What a hazard would hit if it fired now. Panels and vents count units only; the gun hits the first thing in its lane. */
export function hazardTargets(w: World, hz: Hazard): HazardTargets {
  const def = w.c.hazardDefs[hz.def]!;
  if (def.shape === 'lane') {
    const tiles: Pos[] = [];
    for (const p of laneTiles(w, hz)) {
      tiles.push(p);
      const u = w.unitAt(p);
      if (u) return { units: [u], tiles };
      const o = w.blockingObjectAt(p);
      if (o) return { units: [], object: o, tiles };
      if (w.b.blocksProjectile(p)) break;
    }
    return { units: [], tiles };
  }
  const tiles: Pos[] = [];
  for (const t of hz.tiles) for (const p of w.b.square(t, def.radius!)) if (!tiles.some((q) => samePos(q, p))) tiles.push(p);
  tiles.sort((a, b) => a.y - b.y || a.x - b.x);
  const units = w.activeUnits().filter((u) => tiles.some((t) => samePos(t, u.pos))).sort((a, b) => a.id - b.id);
  return { units, tiles };
}

/**
 * The lab ranks targets elite, then the player's life, then soldier, then minion. A soldier is any enemy
 * that is not an elite. A player-side object first in the gun's lane (shield segment or egg) ranks as a minion.
 */
export function classify(tg: HazardTargets): Classified {
  const c: Classified = { elite: false, player: false, soldier: false, playerSide: false, victims: [] };
  for (const u of tg.units) {
    c.victims.push({ kind: 'unit', id: u.id });
    if (u.team === 'enemy') {
      if (u.elite) c.elite = true;
      else c.soldier = true;
    } else {
      c.playerSide = true;
      if (u.id === 0) c.player = true;
    }
  }
  if (tg.object) {
    c.victims.push({ kind: 'object', id: tg.object.id });
    if (tg.object.team === 'player') c.playerSide = true;
  }
  return c;
}

export function hazardSource(hz: Hazard): SourceRef {
  return { side: 'hazard', category: 'hazard', kind: 'hazard', id: hz.id, def: hz.def, via: hz.def };
}

/** Damage a hazard would deal to the player, after armor. */
export function hazardDamageToPlayer(w: World, hz: Hazard): number {
  const def = w.c.hazardDefs[hz.def]!;
  const p = w.player;
  if (!p) return 0;
  const armor = w.c.rules.armorAppliesTo.includes(def.damageType) ? w.armorOf(p) : 0;
  return Math.max(0, def.damage - armor);
}

export interface PrimeDecision {
  hazard: Hazard;
  prime: boolean;
  step: number;
  reason: string;
  victims: EntityRef[];
}

/**
 * The value rule, evaluated for every ready hazard at once. The combined kill check is computed once per
 * priming step, over ready hazards that would hit the player and no elite, in the fixed hazard order.
 * Pure: it reads the state and changes nothing.
 */
export function primeDecisions(w: World): PrimeDecision[] {
  const ready = w.s.hazards.filter((h) => h.state === 'ready');
  const info = ready.map((h) => ({ h, cls: classify(hazardTargets(w, h)) }));
  const player = w.player;
  const kill = info.filter((i) => i.cls.player && !i.cls.elite);
  const total = kill.reduce((sum, i) => sum + hazardDamageToPlayer(w, i.h), 0);
  const combined = !!player && kill.length > 0 && total >= player.hp;
  return info.map(({ h, cls }) => {
    const base = { hazard: h, victims: cls.victims };
    if (cls.elite) return { ...base, prime: false, step: 1, reason: 'An elite would be hit' };
    if (cls.player && combined) return { ...base, prime: true, step: 2, reason: `Combined kill: ${total} damage against ${player!.hp} HP` };
    if (!cls.soldier && cls.playerSide) return { ...base, prime: true, step: 3, reason: 'Only your side would be hit' };
    return { ...base, prime: false, step: 4, reason: cls.soldier ? 'A soldier would be hit' : 'Nothing to hit' };
  });
}

function record(w: World, hz: Hazard, d: Omit<HazardDecision, 'round'>): void {
  const full: HazardDecision = { round: w.s.round, ...d };
  hz.last = full;
  hz.history.push(full);
  if (hz.history.length > 12) hz.history.shift();
}

/** Environment step 2: ready hazards check the value rule and prime (showing their cue) or hold. */
export function primeStep(w: World): void {
  for (const d of primeDecisions(w)) {
    const hz = d.hazard;
    record(w, hz, { stage: 'prime', result: d.prime ? 'prime' : 'hold', step: d.step, reason: d.reason, victims: d.victims });
    if (d.prime) {
      hz.state = 'primed';
      w.emit({ t: 'HazardPrimed', hazard: hz.id, step: d.step, reason: d.reason, victims: d.victims });
      intercomTrigger(w, 'hazardPrimed');
    } else {
      w.emit({ t: 'HazardHeld', hazard: hz.id, step: d.step, reason: d.reason, victims: d.victims });
    }
  }
}

/**
 * Environment step 1: a primed hazard is committed. It fires on everything then in its area, soldiers
 * included, and goes on cooldown. It cancels, with a short cooldown, only if an elite would be hit or
 * nothing would be hit.
 */
export function fireStep(w: World): void {
  for (const hz of w.s.hazards) {
    if (hz.state !== 'primed') continue;
    const def = w.c.hazardDefs[hz.def]!;
    const tg = hazardTargets(w, hz);
    const cls = classify(tg);
    if (cls.elite || cls.victims.length === 0) {
      const reason = cls.elite ? 'An elite would be hit' : 'Nothing to hit';
      hz.state = 'cooldown';
      hz.cooldown = def.cancelCooldown;
      record(w, hz, { stage: 'fire', result: 'cancel', step: null, reason, victims: cls.victims });
      w.emit({ t: 'HazardCancelled', hazard: hz.id, reason, victims: cls.victims });
      continue;
    }
    hz.state = 'cooldown';
    hz.cooldown = def.cooldown;
    record(w, hz, { stage: 'fire', result: 'fire', step: null, reason: 'Fired', victims: cls.victims });
    w.emit({ t: 'HazardFired', hazard: hz.id, victims: cls.victims, tiles: tg.tiles });
    const src = hazardSource(hz);
    if (def.shape === 'lane') {
      fireProjectile(w, hz.tiles[0]!, laneTiles(w, hz), { source: src, damage: def.damage, damageType: def.damageType, reflectable: true });
    } else {
      w.emit({ t: 'AreaEffect', kind: def.damageType === 'electric' ? 'panel' : 'gas', tiles: tg.tiles, source: src });
      for (const u of tg.units) w.damageUnit(u, def.damage, def.damageType, src);
    }
    if (cls.player) intercomTrigger(w, 'hazardHitsPlayer');
    if (cls.soldier) intercomTrigger(w, 'hazardHitsSoldier');
    w.flushDeaths();
  }
}
