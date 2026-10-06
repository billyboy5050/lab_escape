import { DIRS4, addPos, cheb, dirName, samePos } from '../state/grid';
import { tileName } from '../util/tiles';
import type { AIOption, Pos, Unit } from '../state/types';
import { adjacentGoals, approachDest, distanceField, minBy, reachable } from '../rules/pathing';
import { fireProjectile, shotPath, traceProjectile } from '../rules/projectile';
import type { World } from '../rules/world';
import { moveTo, nearestMelee, nearestRanged, stepsTo } from './common';

/**
 * One enemy's action for the round: move first, then act. In the round a wave arrives, its units
 * use only the movement part of their plan.
 */
export function enemyTurn(w: World, e: Unit): void {
  const def = w.c.units[e.def]!;
  const arrival = e.arrivalRound === w.s.round;
  if (def.charge) return wardenTurn(w, e, arrival);
  if (def.sweep) return flamerTurn(w, e, arrival);
  if (def.heal) return medicTurn(w, e, arrival);
  if (def.attack) return guardTurn(w, e, arrival);
  return bruteTurn(w, e, arrival);
}

function decide(w: World, u: Unit, summary: string, options?: AIOption[]): void {
  w.emit({ t: 'AIDecision', id: u.id, def: u.def, summary, options });
}

// ------------------------------------------------------------------------------------------ Guard

export interface GuardShot {
  target: Unit;
}

/**
 * A tile gives a shot at a player-side unit in range and sight when the first thing on the line is on the
 * player's side (that unit, a minion, a shield segment or an egg). An enemy ally, the shutter or the wall gun
 * first on the line spoils it. Returns the nearest such unit (ties: lowest HP, lowest ID).
 */
export function guardShotFrom(w: World, g: Unit, tile: Pos): GuardShot | null {
  const atk = w.c.units[g.def]!.attack!;
  const cands: Unit[] = [];
  for (const u of w.playerSide()) {
    if (cheb(tile, u.pos) > atk.range || !w.b.hasLOS(tile, u.pos)) continue;
    const tr = traceProjectile(w, shotPath(tile, u.pos), { ignoreUnit: g.id });
    const first = tr.hitUnit ? tr.hitUnit.team : tr.hitObject ? tr.hitObject.team : null;
    if (first !== 'player') continue;
    cands.push(u);
  }
  const target = minBy(cands, (u) => [cheb(tile, u.pos), u.hp, u.id]);
  return target ? { target } : null;
}

export function guardTurn(w: World, g: Unit, arrival: boolean): void {
  let dest = g.pos;
  if (guardShotFrom(w, g, g.pos)) {
    decide(w, g, `Holds ${tileName(g.pos)}: it has a shot`);
  } else {
    const reach = reachable(w, g, w.movementAllowance(g));
    const firing = reach.dests
      .filter((d) => !samePos(d, g.pos))
      .map((d) => ({ d, shot: guardShotFrom(w, g, d) }))
      .filter((o): o is { d: Pos; shot: GuardShot } => o.shot !== null);
    const best = minBy(firing, (o) => [stepsTo(w, reach, o.d), cheb(o.d, o.shot.target.pos), o.d.y, o.d.x]);
    if (best) {
      dest = best.d;
      decide(w, g, `Moves to firing tile ${tileName(dest)}`);
    } else {
      const t = nearestRanged(g.pos, w.playerSide());
      if (t) dest = approachDest(w, reach, t.pos);
      decide(w, g, `No firing tile in reach: closes in to ${tileName(dest)}`);
    }
    if (!moveTo(w, g, reach, dest)) return;
  }
  if (arrival) return;
  const shot = guardShotFrom(w, g, g.pos);
  if (!shot) return;
  const atk = w.c.units[g.def]!.attack!;
  fireProjectile(w, g.pos, shotPath(g.pos, shot.target.pos), { source: w.unitSource(g, 'shot'), damage: atk.damage, damageType: atk.damageType, reflectable: true });
}

// ------------------------------------------------------------------------------------------ Medic

function damagedAllies(w: World, m: Unit): Unit[] {
  return w.enemies().filter((u) => u.id !== m.id && u.hp < u.maxHp);
}

export function medicTurn(w: World, m: Unit, arrival: boolean): void {
  const def = w.c.units[m.def]!;
  const heal = def.heal!;
  const safe = def.ai!['safeDistance']!;
  const follow = def.ai!['followDistance']!;
  const reach = reachable(w, m, w.movementAllowance(m));
  const awayFromPlayerSide = (t: Pos) => {
    let d = 9999;
    for (const u of w.playerSide()) d = Math.min(d, cheb(t, u.pos));
    return d;
  };
  // Prefer tiles at least `safe` from the nearest player-side unit (fewest steps); otherwise the farthest.
  const pick = (cands: Pos[]) =>
    minBy(cands, (t) => {
      const d = awayFromPlayerSide(t);
      return d >= safe ? [0, stepsTo(w, reach, t), 0, t.y, t.x] : [1, -d, stepsTo(w, reach, t), t.y, t.x];
    })!;
  const ally = minBy(damagedAllies(w, m), (u) => [u.hp, u.id]);
  let dest = m.pos;
  if (ally) {
    const cands = reach.dests.filter((t) => cheb(t, ally.pos) <= heal.range && w.b.hasLOS(t, ally.pos));
    dest = cands.length ? pick(cands) : approachDest(w, reach, ally.pos);
    decide(w, m, `Goes to heal ${ally.def} ${ally.id} from ${tileName(dest)}`);
  } else {
    const guards = w.enemies().filter((u) => u.def === 'guard');
    const lead = nearestRanged(m.pos, guards.length ? guards : w.enemies().filter((u) => u.id !== m.id));
    if (lead) {
      const cands = reach.dests.filter((t) => cheb(t, lead.pos) <= follow);
      dest = cands.length ? pick(cands) : approachDest(w, reach, lead.pos);
      decide(w, m, `Nobody hurt: follows ${lead.def} ${lead.id} to ${tileName(dest)}`);
    }
  }
  if (!moveTo(w, m, reach, dest) || arrival) return;
  const target = minBy(
    damagedAllies(w, m).filter((u) => cheb(u.pos, m.pos) <= heal.range && w.b.hasLOS(m.pos, u.pos)),
    (u) => [u.hp, u.id],
  );
  if (target) w.heal(target, heal.amount, w.unitSource(m, 'heal'));
}

// ------------------------------------------------------------------------------------------ Flamer

/** The sweep line: up to `length` tiles in a straight line starting adjacent. Walls and pillars end it. */
export function sweepTiles(w: World, from: Pos, dir: Pos, length: number): Pos[] {
  const out: Pos[] = [];
  for (let k = 1; k <= length; k++) {
    const p = addPos(from, dir, k);
    if (w.b.blocksMove(p)) break;
    out.push(p);
  }
  return out;
}

/** Scores every reachable tile and sweep direction: +2 per player-side unit, +4 more for the player, -3 per ally. */
export function flamerOptions(w: World, f: Unit, tiles: Pos[], steps: (p: Pos) => number): AIOption[] {
  const def = w.c.units[f.def]!;
  const ai = def.ai!;
  const perSide = ai['scorePerPlayerSideHit']!;
  const playerBonus = ai['scorePlayerBonus']!;
  const perAlly = ai['scorePerAllyHit']!;
  const units = w.activeUnits().filter((u) => u.id !== f.id);
  const out: AIOption[] = [];
  for (const t of tiles) {
    for (const d of DIRS4) {
      const line = sweepTiles(w, t, d, def.sweep!.length);
      const hits = units.filter((u) => line.some((p) => samePos(p, u.pos)));
      let score = 0;
      for (const u of hits) {
        if (u.team === 'player') score += perSide + (u.id === 0 ? playerBonus : 0);
        else score += perAlly;
      }
      out.push({ tile: t, dir: d, score, steps: steps(t), hits: hits.map((u) => u.id) });
    }
  }
  return out;
}

/** Highest positive score; ties: shortest move, then lowest tile, then direction order N, E, S, W. */
export function bestFlamerOption(opts: AIOption[]): AIOption | undefined {
  return minBy(
    opts.filter((o) => o.score > 0),
    (o) => [-o.score, o.steps, o.tile.y, o.tile.x, DIRS4.findIndex((d) => samePos(d, o.dir))],
  );
}

export function flamerTurn(w: World, f: Unit, arrival: boolean): void {
  const reach = reachable(w, f, w.movementAllowance(f));
  const opts = flamerOptions(w, f, reach.dests, (p) => stepsTo(w, reach, p));
  const best = bestFlamerOption(opts);
  let dest = f.pos;
  if (best) {
    dest = best.tile;
    decide(w, f, `Best sweep scores ${best.score}: from ${tileName(best.tile)} facing ${dirName(best.dir)}`, opts.filter((o) => o.score !== 0));
  } else {
    const t = nearestMelee(w, f.pos, w.playerSide());
    if (t) dest = approachDest(w, reach, t.pos);
    decide(w, f, `No positive sweep: moves toward ${t ? `${t.def} ${t.id}` : 'nobody'}`, []);
  }
  if (!moveTo(w, f, reach, dest) || arrival) return;
  const here = bestFlamerOption(flamerOptions(w, f, [f.pos], () => 0));
  if (!here) return;
  const sw = w.c.units[f.def]!.sweep!;
  const tiles = sweepTiles(w, f.pos, here.dir, sw.length);
  const src = w.unitSource(f, 'sweep');
  w.emit({ t: 'AreaEffect', kind: 'sweep', tiles, source: src });
  w.areaDamage(tiles, sw.damage, sw.damageType, src, { exclude: f.id });
}

// ------------------------------------------------------------------------------------------ Warden

export interface ChargePlan {
  dir: Pos;
  /** The player-side unit 2 to 5 tiles away that makes the lane worth charging. */
  trigger: Unit;
  /** Distance to the first body or object in the lane; the Warden stops just before it. */
  stopAt: number;
}

/**
 * Charge lanes from a tile. A player-side unit must stand minDistance to maxMove+1 tiles away in a straight
 * orthogonal line, with no wall, pillar, blocking terrain or enemy ally in the lane. The Warden stops against
 * the first player-side unit or blocking object, and must move at least one tile.
 */
export function chargePlans(w: World, wd: Unit, from: Pos, only?: (u: Unit) => boolean, maxMove = chargeMove(w, wd)): ChargePlan[] {
  const ch = w.c.units[wd.def]!.charge!;
  const plans: ChargePlan[] = [];
  if (maxMove < 1) return plans;
  for (const d of DIRS4) {
    let stopAt = 0;
    for (let k = 1; k <= maxMove + 1; k++) {
      const p = addPos(from, d, k);
      if (w.b.blocksMove(p)) break;
      const u = w.unitAt(p);
      if (u && u.id === wd.id) continue;
      if (u && u.team === 'enemy') break;
      if (!stopAt && (u || w.blockingObjectAt(p))) stopAt = k;
      if (stopAt === 1) break;
      if (u && u.team === 'player' && k >= ch.minDistance && (!only || only(u))) {
        plans.push({ dir: d, trigger: u, stopAt });
        break;
      }
    }
  }
  return plans;
}

/** Charge distance this round: Pinned stops it, Slowed shortens it. */
export function chargeMove(w: World, wd: Unit): number {
  const ch = w.c.units[wd.def]!.charge!;
  if (wd.statuses.pinned) return 0;
  return ch.maxMove - (wd.statuses.slowed ? w.c.rules.statuses.slowed.movementPenalty : 0);
}

export function bestCharge(plans: ChargePlan[], from: Pos): ChargePlan | undefined {
  return minBy(plans, (p) => [cheb(from, p.trigger.pos), p.trigger.hp, p.trigger.id]);
}

function performCharge(w: World, wd: Unit, dir: Pos): void {
  const ch = w.c.units[wd.def]!.charge!;
  const from = { ...wd.pos };
  const path: Pos[] = [];
  let cur = wd.pos;
  const maxMove = chargeMove(w, wd);
  for (let k = 0; k < maxMove; k++) {
    const next = addPos(cur, dir);
    if (w.b.blocksMove(next) || w.unitAt(next) || w.blockingObjectAt(next)) break;
    path.push(next);
    cur = next;
  }
  if (!w.moveAlong(wd, path, 'charge')) return;
  const front = addPos(wd.pos, dir);
  const hitUnit = w.unitAt(front);
  const hitObj = hitUnit ? undefined : w.blockingObjectAt(front);
  const src = w.unitSource(wd, 'charge');
  w.emit({ t: 'Charged', id: wd.id, from, to: { ...wd.pos }, dir, hit: hitUnit ? { kind: 'unit', id: hitUnit.id } : hitObj ? { kind: 'object', id: hitObj.id } : null });
  if (hitUnit && hitUnit.team === 'player') w.damageUnit(hitUnit, ch.damage, ch.damageType, src);
  else if (hitObj) w.damageObject(hitObj, ch.damage, ch.damageType, src);
}

function melee(w: World, attacker: Unit, target: Unit): void {
  const m = w.c.units[attacker.def]!.melee!;
  w.damageUnit(target, m.damage, m.damageType, w.unitSource(attacker, 'melee'));
}

export function wardenTurn(w: World, wd: Unit, arrival: boolean): void {
  const def = w.c.units[wd.def]!;
  if (!arrival && def.purge && (wd.cooldowns['purge'] ?? 0) === 0 && wd.statuses.poison) {
    w.removeStatus(wd, 'poison', 'purge');
    wd.cooldowns['purge'] = def.purge.cooldown;
    wd.purgeImmuneRound = w.s.round;
    w.emit({ t: 'Purged', id: wd.id });
  }
  const player = w.player;
  if (!arrival) {
    const plan = bestCharge(chargePlans(w, wd, wd.pos), wd.pos);
    if (plan) {
      decide(w, wd, `Charges ${dirName(plan.dir)} at ${plan.trigger.def} ${plan.trigger.id}`);
      return performCharge(w, wd, plan.dir);
    }
    if (player && cheb(wd.pos, player.pos) === 1) {
      decide(w, wd, 'Melees the intruder');
      return melee(w, wd, player);
    }
  }
  if (!player) return;
  const reach = reachable(w, wd, w.movementAllowance(wd));
  const field = distanceField(w, adjacentGoals(w, player.pos));
  const at = (p: Pos) => field[w.b.idx(p)]!;
  const closer = reach.dests.filter((t) => at(t) < at(wd.pos));
  if (!closer.length) {
    if (arrival) return;
    const target = minBy(w.playerSide().filter((u) => cheb(u.pos, wd.pos) === 1), (u) => [1, u.hp, u.id]);
    decide(w, wd, target ? `Cannot get closer: melees ${target.def} ${target.id}` : 'Cannot get closer and nothing adjacent');
    if (target) melee(w, wd, target);
    return;
  }
  // Lining up a lane for next round: statuses that limit this round's charge will have worn off.
  const fullCharge = def.charge!.maxMove;
  const lane = closer.filter((t) => chargePlans(w, wd, t, (u) => u.id === 0, fullCharge).length > 0);
  const pool = lane.length ? lane : closer;
  const dest = minBy(pool, (t) => [at(t), stepsTo(w, reach, t), t.y, t.x])!;
  decide(w, wd, lane.length ? `Approaches to ${tileName(dest)}, lining up a charge lane` : `Approaches to ${tileName(dest)}`);
  moveTo(w, wd, reach, dest);
}

/** Fallback for an enemy with only a melee attack (not used by the MVP roster). */
function bruteTurn(w: World, e: Unit, arrival: boolean): void {
  const t = nearestMelee(w, e.pos, w.playerSide());
  if (!t) return;
  if (cheb(e.pos, t.pos) > 1) {
    const reach = reachable(w, e, w.movementAllowance(e));
    if (!moveTo(w, e, reach, approachDest(w, reach, t.pos))) return;
  }
  if (arrival || !w.c.units[e.def]!.melee) return;
  const adj = minBy(w.playerSide().filter((u) => cheb(u.pos, e.pos) === 1), (u) => [u.hp, u.id]);
  if (adj) melee(w, e, adj);
}
