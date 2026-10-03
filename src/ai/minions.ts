import { cheb } from '../state/grid';
import type { Pos, Unit } from '../state/types';
import { approachDest, minBy, reachable } from '../rules/pathing';
import { fireProjectile, shotPath } from '../rules/projectile';
import { UP, type World } from '../rules/world';
import { moveTo, nearestMelee, nearestRanged, stepsTo } from './common';

/**
 * One minion's action for the round: move first, then act. Minions never pick a friendly unit as a
 * target; friendly fire comes from blast areas, chains and projectile lines.
 */
export function minionTurn(w: World, m: Unit): void {
  if (m.droneState === 'docked') return;
  const def = w.c.units[m.def]!;
  if (def.ammo !== undefined) return turretTurn(w, m);
  if (def.zap) return droneTurn(w, m);
  if (def.blast) return bursterTurn(w, m);
  if (def.attack) return spitterTurn(w, m);
  if (def.melee) return hatchlingTurn(w, m);
}

/** Fires at the nearest enemy within range and sight, using 1 ammo. At 0 ammo it stays as a blocker. */
export function turretTurn(w: World, m: Unit): void {
  const atk = w.c.units[m.def]!.attack!;
  if ((m.ammo ?? 0) <= 0) return;
  const target = nearestRanged(m.pos, w.enemies().filter((e) => cheb(e.pos, m.pos) <= atk.range && w.b.hasLOS(m.pos, e.pos)));
  if (!target) return;
  m.ammo = (m.ammo ?? 0) - 1;
  const src = w.unitSource(m, 'turret_shot');
  const slow = w.hasUp('T3') ? w.up('T3').status : undefined;
  fireProjectile(w, m.pos, shotPath(m.pos, target.pos), {
    source: src,
    damage: atk.damage,
    damageType: atk.damageType,
    armorIgnore: w.piercing(atk.damageType),
    reflectable: true,
    onHitUnit: slow
      ? (u) => {
          if (u.team === 'enemy') w.applyStatus(u, slow.id, slow.duration, { ...src, via: UP.T3 });
        }
      : undefined,
  });
}

/**
 * Drone cycle. Deployed: fly toward the nearest enemy until it is within zap range with sight, then zap it.
 * Returning (0 charges): fly toward the player without attacking, and dock when adjacent or after the
 * set number of returning phases.
 */
export function droneTurn(w: World, m: Unit): void {
  const def = w.c.units[m.def]!;
  const zap = def.zap!;
  const player = w.player;
  if (m.droneState === 'returning') {
    if (player && cheb(m.pos, player.pos) <= 1) return dock(w, m);
    if (player) {
      const reach = reachable(w, m, w.movementAllowance(m, def.returnMove ?? def.move));
      const dest = minBy(reach.dests, (t) => [cheb(t, player.pos), stepsTo(w, reach, t), t.y, t.x])!;
      if (!moveTo(w, m, reach, dest)) return;
    }
    m.returningPhases = (m.returningPhases ?? 0) + 1;
    if ((player && cheb(m.pos, player.pos) <= 1) || m.returningPhases >= (def.returnPhases ?? 2)) dock(w, m);
    return;
  }
  const inRange = (t: Pos, e: Unit) => cheb(t, e.pos) <= zap.range && w.b.hasLOS(t, e.pos);
  const target = nearestRanged(m.pos, w.enemies());
  if (target && !inRange(m.pos, target)) {
    const reach = reachable(w, m, w.movementAllowance(m));
    const good = reach.dests.filter((t) => inRange(t, target));
    const dest = good.length
      ? minBy(good, (t) => [stepsTo(w, reach, t), cheb(t, target.pos), t.y, t.x])!
      : minBy(reach.dests, (t) => [cheb(t, target.pos), stepsTo(w, reach, t), t.y, t.x])!;
    if (!moveTo(w, m, reach, dest)) return;
  }
  const zt = nearestRanged(m.pos, w.enemies().filter((e) => inRange(m.pos, e)));
  if (!zt) return;
  droneZap(w, m, zt);
  m.charges = (m.charges ?? 1) - 1;
  if (m.charges <= 0) {
    m.droneState = 'returning';
    m.returningPhases = 0;
  }
}

/**
 * Zap: electric damage to the target, chaining to every other unit adjacent to it (never the drone itself).
 * Overcharged Drone adds hops to units adjacent to chained targets; Plague Drone poisons everything hit.
 */
export function droneZap(w: World, m: Unit, target: Unit): void {
  const zap = w.c.units[m.def]!.zap!;
  const src = w.unitSource(m, 'zap');
  const hops = w.hasUp('T6') ? w.up('T6').extraChainHops ?? 0 : 0;
  const hit = new Set<number>([target.id]);
  const chained: Unit[] = [];
  let frontier: Unit[] = [target];
  w.emit({ t: 'ChainArc', from: { ...m.pos }, to: { ...target.pos }, source: src });
  for (let h = 0; h <= hops; h++) {
    const next = w
      .activeUnits()
      .filter((u) => u.id !== m.id && !hit.has(u.id) && frontier.some((f) => cheb(f.pos, u.pos) === 1))
      .sort((a, b) => a.id - b.id);
    for (const u of next) {
      hit.add(u.id);
      chained.push(u);
      const via = frontier.find((f) => cheb(f.pos, u.pos) === 1)!;
      w.emit({ t: 'ChainArc', from: { ...via.pos }, to: { ...u.pos }, source: src });
    }
    frontier = next;
  }
  w.damageUnit(target, zap.damage, zap.damageType, src);
  for (const u of chained) w.damageUnit(u, zap.chainDamage, zap.damageType, src);
  if (w.hasUp('H2')) {
    const st = w.up('H2').status!;
    const psrc = { ...src, via: UP.H2 };
    for (const u of [target, ...chained]) w.applyStatus(u, st.id, st.duration, psrc);
  }
}

function dock(w: World, m: Unit): void {
  m.droneState = 'docked';
  m.returningPhases = 0;
  w.emit({ t: 'DroneDocked', id: m.id, pos: { ...m.pos } });
}

/** Basic hatchling: moves toward the nearest enemy and bites one adjacent enemy. */
export function hatchlingTurn(w: World, m: Unit): void {
  const target = nearestMelee(w, m.pos, w.enemies());
  if (!target) return;
  if (cheb(m.pos, target.pos) > 1) {
    const reach = reachable(w, m, w.movementAllowance(m));
    if (!moveTo(w, m, reach, approachDest(w, reach, target.pos))) return;
  }
  const bite = minBy(w.enemies().filter((e) => cheb(e.pos, m.pos) === 1), (e) => [e.hp, e.id]);
  const melee = w.c.units[m.def]!.melee!;
  if (bite) w.damageUnit(bite, melee.damage, melee.damageType, w.unitSource(m, 'bite'));
}

/** Spitter: moves until an enemy is within range and sight, then spits acid at the nearest one. */
export function spitterTurn(w: World, m: Unit): void {
  const atk = w.c.units[m.def]!.attack!;
  const shotFrom = (t: Pos) => nearestRanged(t, w.enemies().filter((e) => cheb(e.pos, t) <= atk.range && w.b.hasLOS(t, e.pos)));
  if (!shotFrom(m.pos)) {
    const reach = reachable(w, m, w.movementAllowance(m));
    const good = reach.dests.map((t) => ({ t, s: shotFrom(t) })).filter((x): x is { t: Pos; s: Unit } => !!x.s);
    let dest = m.pos;
    if (good.length) dest = minBy(good, (x) => [stepsTo(w, reach, x.t), cheb(x.t, x.s.pos), x.t.y, x.t.x])!.t;
    else {
      const t = nearestRanged(m.pos, w.enemies());
      if (t) dest = approachDest(w, reach, t.pos);
    }
    if (!moveTo(w, m, reach, dest)) return;
  }
  const t = shotFrom(m.pos);
  if (t) fireProjectile(w, m.pos, shotPath(m.pos, t.pos), { source: w.unitSource(m, 'spit'), damage: atk.damage, damageType: atk.damageType, reflectable: true });
}

/** Burster: moves toward the nearest enemy, and when adjacent to one, detonates and dies. */
export function bursterTurn(w: World, m: Unit): void {
  const adjacentEnemy = () => w.enemies().some((e) => cheb(e.pos, m.pos) === 1);
  if (!adjacentEnemy()) {
    const target = nearestMelee(w, m.pos, w.enemies());
    if (!target) return;
    const reach = reachable(w, m, w.movementAllowance(m));
    if (!moveTo(w, m, reach, approachDest(w, reach, target.pos))) return;
  }
  if (adjacentEnemy()) detonate(w, m);
}

function detonate(w: World, m: Unit): void {
  const blast = w.c.units[m.def]!.blast!;
  const src = w.unitSource(m, 'burst');
  m.hp = 0;
  m.dead = true;
  m.killedBy = src;
  const tiles = w.b.square(m.pos, blast.radius);
  w.emit({ t: 'AreaEffect', kind: 'blast', tiles, source: src });
  w.areaDamage(tiles, blast.damage, blast.damageType, src, { exclude: m.id });
}
