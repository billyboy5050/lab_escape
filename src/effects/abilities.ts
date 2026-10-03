import { DIRS4, addPos, cheb, samePos } from '../state/grid';
import type { Pos, Unit } from '../state/types';
import { fireProjectile, shotPath, traceProjectile, type ShotPayload } from '../rules/projectile';
import { UP, type World } from '../rules/world';

export interface TargetOption {
  target: Pos;
  target2?: Pos | null;
  mode?: 'self' | 'unit';
}

export interface AbilityImpl {
  /** Every valid target option for the player right now. Empty means the ability cannot be committed. */
  options(w: World, p: Unit): TargetOption[];
  resolve(w: World, p: Unit, opt: TargetOption): void;
  /** Cap or availability check that disables the ability regardless of targets. */
  blocked?(w: World): string | null;
}

/** Tiles within a Chebyshev range of the player, row-major. */
function tilesWithin(w: World, center: Pos, range: number): Pos[] {
  return w.b.square(center, range);
}

/**
 * Projectile abilities can target any unit in range and sight. The shot itself hits the first unit or
 * blocking object on the line, friendly or not. Targets whose line ends in a shutter or the wall gun are not offered.
 */
function projectileOptions(w: World, p: Unit, range: number, extraTargets: Pos[] = []): TargetOption[] {
  const out: TargetOption[] = [];
  for (const u of w.activeUnits()) {
    if (u.id === p.id || cheb(p.pos, u.pos) > range || !w.b.hasLOS(p.pos, u.pos)) continue;
    const tr = traceProjectile(w, shotPath(p.pos, u.pos));
    if (tr.hitUnit || tr.hitObject) out.push({ target: { ...u.pos } });
  }
  for (const t of extraTargets) {
    if (out.some((o) => samePos(o.target, t))) continue;
    out.push({ target: { ...t } });
  }
  return out.sort((a, b) => a.target.y - b.target.y || a.target.x - b.target.x);
}

function shoot(w: World, p: Unit, target: Pos, payload: ShotPayload): void {
  fireProjectile(w, p.pos, shotPath(p.pos, target), payload);
}

const sidearm: AbilityImpl = {
  options(w, p) {
    const def = w.c.abilities['sidearm']!;
    const range = def.targeting.range!;
    const mines: Pos[] = [];
    if (w.hasUp('T2')) {
      for (const m of w.s.objects) {
        if (m.kind !== 'mine' || m.destroyed || cheb(p.pos, m.pos) > range || !w.b.hasLOS(p.pos, m.pos)) continue;
        const tr = traceProjectile(w, shotPath(p.pos, m.pos), { mineTarget: m.pos });
        if (tr.mine || tr.hitUnit || tr.hitObject) mines.push(m.pos);
      }
    }
    return projectileOptions(w, p, range, mines);
  },
  resolve(w, p, opt) {
    const def = w.c.abilities['sidearm']!;
    const mineTarget = w.hasUp('T2') && w.mineAt(opt.target) ? opt.target : undefined;
    shoot(w, p, opt.target, {
      source: w.unitSource(p, 'sidearm'),
      damage: def.damage,
      damageType: def.damageType,
      armorIgnore: w.piercing(def.damageType),
      mineTarget,
      reflectable: true,
    });
  },
};

const acidSpit: AbilityImpl = {
  options: (w, p) => projectileOptions(w, p, w.c.abilities['acid_spit']!.targeting.range!),
  resolve(w, p, opt) {
    const def = w.c.abilities['acid_spit']!;
    shoot(w, p, opt.target, { source: w.unitSource(p, 'acid_spit'), damage: def.damage, damageType: def.damageType, corrode: def.corrode, reflectable: true });
  },
};

const parasite: AbilityImpl = {
  options: (w, p) => projectileOptions(w, p, w.c.abilities['parasite']!.targeting.range!),
  resolve(w, p, opt) {
    const def = w.c.abilities['parasite']!;
    shoot(w, p, opt.target, { source: w.unitSource(p, 'parasite'), status: def.status, reflectable: true });
  },
};

/** The grapple's pull-unit target: the first thing on the line must be a pullable unit with room to move. */
function grappleUnitHit(w: World, p: Unit, target: Pos): { unit: Unit; drag: Pos[] } | null {
  const path = shotPath(p.pos, target);
  const tr = traceProjectile(w, path);
  if (!tr.hitUnit || tr.hitUnit.elite) return null;
  const k = tr.path.length - 1;
  if (k < 1) return null;
  return { unit: tr.hitUnit, drag: path.slice(0, k).reverse() };
}

/** Pull self: the tiles the player crosses, stopping on the last free tile before a blocker. */
export function grappleSelfPath(w: World, p: Unit, target: Pos): Pos[] {
  const walk: Pos[] = [];
  for (const t of shotPath(p.pos, target)) {
    if (!w.isFree(t, p.id)) break;
    walk.push(t);
  }
  return walk;
}

const grapple: AbilityImpl = {
  options(w, p) {
    const range = w.c.abilities['grapple_hook']!.targeting.range!;
    const out: TargetOption[] = [];
    for (const t of tilesWithin(w, p.pos, range)) {
      if (samePos(t, p.pos) || !w.b.hasLOS(p.pos, t)) continue;
      if (grappleSelfPath(w, p, t).length > 0) out.push({ target: t, mode: 'self' });
      const u = w.unitAt(t);
      if (u && u.id !== p.id && grappleUnitHit(w, p, t)) out.push({ target: t, mode: 'unit' });
    }
    return out;
  },
  resolve(w, p, opt) {
    const src = w.unitSource(p, 'grapple_hook');
    if (opt.mode === 'self') {
      const walk = grappleSelfPath(w, p, opt.target);
      w.emit({ t: 'ProjectileFired', source: src, from: { ...p.pos }, path: shotPath(p.pos, opt.target), hit: null, hitPos: null });
      w.moveAlong(p, walk, 'grapple');
      return;
    }
    const hit = grappleUnitHit(w, p, opt.target);
    const tr = traceProjectile(w, shotPath(p.pos, opt.target));
    w.emit({ t: 'ProjectileFired', source: src, from: { ...p.pos }, path: tr.path, hit: tr.hitUnit ? { kind: 'unit', id: tr.hitUnit.id } : null, hitPos: tr.hitPos });
    if (!hit) return;
    const u = hit.unit;
    w.moveAlong(u, hit.drag, 'pull');
    if (!u.dead && w.hasUp('T4')) {
      const up = w.up('T4');
      w.immediate(`u${p.id}`, UP.T4, () => {
        const psrc = w.playerEffect(UP.T4);
        w.damageUnit(u, up.damage ?? 0, up.damageType ?? 'kinetic', psrc);
        if (up.status) w.applyStatus(u, up.status.id, up.status.duration, psrc);
      });
    }
  },
};

const mineCapBlock = (w: World) =>
  w.s.objects.filter((o) => o.kind === 'mine').length >= w.c.rules.caps.mines ? `Mine cap reached (${w.c.rules.caps.mines})` : null;

const proximityMine: AbilityImpl = {
  blocked: mineCapBlock,
  options(w, p) {
    if (mineCapBlock(w)) return [];
    return tilesWithin(w, p.pos, w.c.abilities['proximity_mine']!.targeting.range!).filter((t) => w.isEmpty(t)).map((t) => ({ target: t }));
  },
  resolve(w, p, opt) {
    w.placeObject('mine', opt.target, { team: 'player' }, w.unitSource(p, 'proximity_mine'));
  },
};

const minionCapBlock = (w: World) => (w.minionCount() >= w.c.rules.caps.minions ? `Minion cap reached (${w.c.rules.caps.minions})` : null);

const autoTurret: AbilityImpl = {
  blocked: minionCapBlock,
  options(w, p) {
    if (minionCapBlock(w)) return [];
    return tilesWithin(w, p.pos, w.c.abilities['auto_turret']!.targeting.range!).filter((t) => w.isEmpty(t)).map((t) => ({ target: t }));
  },
  resolve(w, _p, opt) {
    w.spawnUnit(w.c.abilities['auto_turret']!.summon!, opt.target, 'auto_turret');
  },
};

const scoutDrone: AbilityImpl = {
  blocked: minionCapBlock,
  options(w, p) {
    if (minionCapBlock(w)) return [];
    return w.b.neighbors8(p.pos).filter((t) => w.isFree(t)).map((t) => ({ target: t }));
  },
  resolve(w, _p, opt) {
    w.spawnUnit(w.c.abilities['scout_drone']!.summon!, opt.target, 'scout_drone');
  },
};

function activeShieldPlacements(w: World): number {
  return new Set(w.s.objects.filter((o) => o.kind === 'shield').map((o) => o.placement)).size;
}

const shieldCapBlock = (w: World) =>
  activeShieldPlacements(w) >= w.c.rules.caps.shieldPlacements ? `Shield cap reached (${w.c.rules.caps.shieldPlacements} placements)` : null;

const barrierShield: AbilityImpl = {
  blocked: shieldCapBlock,
  options(w, p) {
    if (shieldCapBlock(w)) return [];
    const tiles = tilesWithin(w, p.pos, w.c.abilities['barrier_shield']!.targeting.range!).filter((t) => w.isEmpty(t));
    const out: TargetOption[] = [];
    for (let i = 0; i < tiles.length; i++) {
      for (let j = i + 1; j < tiles.length; j++) {
        if (cheb(tiles[i]!, tiles[j]!) === 1) out.push({ target: tiles[i]!, target2: tiles[j]! });
      }
    }
    return out;
  },
  resolve(w, p, opt) {
    const hp = w.c.abilities['barrier_shield']!.segmentHp!;
    const placement = w.s.nextPlacementId++;
    const src = w.unitSource(p, 'barrier_shield');
    for (const t of [opt.target, opt.target2!]) w.placeObject('shield', t, { team: 'player', hp, maxHp: hp, placement }, src);
  },
};

/** Lunge landings: up to 3 tiles in a straight orthogonal line, over units, not over terrain or shields and eggs. */
export function lungeLandings(w: World, p: Unit): Pos[] {
  const range = w.c.abilities['lunge']!.targeting.range!;
  const out: Pos[] = [];
  for (const d of DIRS4) {
    for (let k = 1; k <= range; k++) {
      const t = addPos(p.pos, d, k);
      if (w.b.blocksMove(t) || w.blockingObjectAt(t)) break;
      if (w.unitAt(t)) continue;
      out.push(t);
    }
  }
  return out;
}

const lunge: AbilityImpl = {
  options(w, p) {
    const out: TargetOption[] = [];
    for (const land of lungeLandings(w, p)) {
      out.push({ target: land, target2: null });
      for (const u of w.activeUnits()) {
        if (u.id !== p.id && cheb(u.pos, land) === 1) out.push({ target: land, target2: { ...u.pos } });
      }
    }
    return out;
  },
  resolve(w, p, opt) {
    const def = w.c.abilities['lunge']!;
    if (!w.moveAlong(p, [opt.target], 'lunge')) return;
    if (!opt.target2) return;
    const u = w.unitAt(opt.target2);
    if (u && u.id !== p.id && cheb(u.pos, p.pos) === 1) w.damageUnit(u, def.damage!, def.damageType!, w.unitSource(p, 'lunge'));
  },
};

const sporePod: AbilityImpl = {
  options(w, p) {
    const def = w.c.abilities['spore_pod']!;
    const out: TargetOption[] = [];
    for (const t of tilesWithin(w, p.pos, def.targeting.range!)) {
      const area = w.b.square(t, def.area!.radius);
      if (w.activeUnits().some((u) => area.some((a) => samePos(a, u.pos)))) out.push({ target: t });
    }
    return out;
  },
  resolve(w, p, opt) {
    const def = w.c.abilities['spore_pod']!;
    const tiles = w.b.square(opt.target, def.area!.radius);
    const src = w.unitSource(p, 'spore_pod');
    w.emit({ t: 'AreaEffect', kind: 'spore', tiles, source: src });
    for (const u of w.activeUnits().filter((x) => tiles.some((t) => samePos(t, x.pos))).sort((a, b) => a.id - b.id)) {
      w.applyStatus(u, def.status!.id, def.status!.duration, src);
    }
  },
};

/** What an egg on this tile would hatch into, or null if the tile cannot take an egg. */
export function eggPlan(w: World, t: Pos): { host: string; hatchInto: string; timer: number; count: number; corpseId?: number } | null {
  const eggs = w.c.rules.eggs;
  if (w.isEmpty(t)) return { host: 'floor', hatchInto: eggs.floorVariant, timer: eggs.floorTimer, count: eggs.floorHatchlings };
  const objs = w.objectsAt(t);
  const corpse = objs.find((o) => o.kind === 'corpse');
  if (!corpse || objs.length !== 1 || w.unitAt(t) || w.b.blocksMove(t)) return null;
  const variant = eggs.corpseHosts[corpse.of!];
  if (!variant) return null;
  const timer = w.hasUp('A4') ? w.up('A4').corpseTimer ?? eggs.corpseTimer : eggs.corpseTimer;
  return { host: corpse.of!, hatchInto: variant, timer, count: 1, corpseId: corpse.id };
}

const eggCapBlock = (w: World) =>
  w.s.objects.filter((o) => o.kind === 'egg').length >= w.c.rules.caps.eggs ? `Egg cap reached (${w.c.rules.caps.eggs})` : null;

const broodEgg: AbilityImpl = {
  blocked: eggCapBlock,
  options(w, p) {
    if (eggCapBlock(w)) return [];
    return tilesWithin(w, p.pos, w.c.abilities['brood_egg']!.targeting.range!).filter((t) => eggPlan(w, t)).map((t) => ({ target: t }));
  },
  resolve(w, p, opt) {
    const plan = eggPlan(w, opt.target)!;
    if (plan.corpseId !== undefined) {
      const corpse = w.s.objects.find((o) => o.id === plan.corpseId)!;
      w.removeObject(corpse, 'consumed');
    }
    const hp = w.c.rules.eggs.hp;
    w.placeObject('egg', opt.target, { team: 'player', hp, maxHp: hp, timer: plan.timer, host: plan.host, hatchInto: plan.hatchInto }, w.unitSource(p, 'brood_egg'));
  },
};

export const ABILITIES: Record<string, AbilityImpl> = {
  sidearm,
  grapple_hook: grapple,
  proximity_mine: proximityMine,
  auto_turret: autoTurret,
  scout_drone: scoutDrone,
  barrier_shield: barrierShield,
  acid_spit: acidSpit,
  lunge,
  spore_pod: sporePod,
  brood_egg: broodEgg,
  parasite,
};

export function sameOption(a: TargetOption, b: TargetOption): boolean {
  if (!samePos(a.target, b.target)) return false;
  if ((a.mode ?? null) !== (b.mode ?? null)) return false;
  const a2 = a.target2 ?? null;
  const b2 = b.target2 ?? null;
  if (a2 === null || b2 === null) return a2 === b2;
  return samePos(a2, b2);
}

/** Barrier Shield pairs are unordered: either tile may be given first. */
export function matchOption(options: TargetOption[], want: TargetOption): TargetOption | undefined {
  return options.find((o) => sameOption(o, want) || (o.target2 && want.target2 && samePos(o.target, want.target2) && samePos(o.target2, want.target) && o.mode === want.mode));
}
