import { lineTiles, samePos } from '../state/grid';
import type { DamageType, EntityRef, GameObject, Pos, SourceRef, StatusId, Unit } from '../state/types';
import { UP, type World } from './world';

export interface ShotPayload {
  source: SourceRef;
  damage?: number;
  damageType?: DamageType;
  armorIgnore?: number;
  corrode?: number;
  status?: { id: StatusId; duration: number };
  /** Runs after the hit lands on a unit that survives it (Suppressing Fire). */
  onHitUnit?: (u: Unit) => void;
  /** Sidearm with Shot-Triggered Mines aimed at this mine tile. Other shots pass over mines. */
  mineTarget?: Pos;
  /** Damaging shots can be reflected by a Reflective Shield. The grapple cannot. */
  reflectable: boolean;
}

export interface Trace {
  /** Tiles the projectile crossed, ending with the tile it stopped on. */
  path: Pos[];
  hitUnit?: Unit;
  hitObject?: GameObject;
  mine?: GameObject;
  /** Stopped by terrain that blocks projectiles (shutter, wall gun, pillar, wall). */
  blocked?: boolean;
  hitPos: Pos | null;
}

/** The tiles a projectile crosses from `from` toward `to`, excluding the shooter's own tile. */
export function shotPath(from: Pos, to: Pos): Pos[] {
  return lineTiles(from, to).slice(1);
}

/** Walks a projectile path and finds the first unit, blocking object, blocking terrain or aimed mine. */
export function traceProjectile(w: World, path: Pos[], opts: { mineTarget?: Pos; ignoreUnit?: number } = {}): Trace {
  const crossed: Pos[] = [];
  for (const p of path) {
    crossed.push(p);
    const u = w.unitAt(p);
    if (u && u.id !== opts.ignoreUnit) return { path: crossed, hitUnit: u, hitPos: p };
    const o = w.blockingObjectAt(p);
    if (o) return { path: crossed, hitObject: o, hitPos: p };
    if (w.b.blocksProjectile(p)) return { path: crossed, blocked: true, hitPos: p };
    if (opts.mineTarget && samePos(p, opts.mineTarget)) {
      const m = w.mineAt(p);
      if (m) return { path: crossed, mine: m, hitPos: p };
    }
  }
  return { path: crossed, hitPos: null };
}

export function traceHitRef(t: Trace): EntityRef | null {
  if (t.hitUnit) return { kind: 'unit', id: t.hitUnit.id };
  if (t.hitObject) return { kind: 'object', id: t.hitObject.id };
  if (t.mine) return { kind: 'object', id: t.mine.id };
  return null;
}

/**
 * Fires a projectile along a path and applies what it hits: the first unit or blocking object, friendly
 * or not. A shield segment with Reflective Shield sends the first projectile each round back along its line.
 */
export function fireProjectile(w: World, from: Pos, path: Pos[], payload: ShotPayload, reflected = false): Trace {
  const tr = traceProjectile(w, path, { mineTarget: reflected ? undefined : payload.mineTarget });
  w.emit({ t: 'ProjectileFired', source: payload.source, from: { ...from }, path: tr.path, hit: traceHitRef(tr), hitPos: tr.hitPos, reflected });
  if (tr.mine) {
    w.triggerMine(tr.mine, null);
    return tr;
  }
  const o = tr.hitObject;
  if (o) {
    if (o.kind === 'shield' && payload.reflectable && w.hasUp('T5') && o.reflectedRound !== w.s.round) {
      const back = tr.path.slice(0, -1).reverse().concat([{ ...from }]);
      w.immediate(`o${o.id}`, UP.T5, () => {
        o.reflectedRound = w.s.round;
        fireProjectile(w, o.pos, back, payload, true);
      });
      return tr;
    }
    if (payload.damage) w.damageObject(o, payload.damage, payload.damageType!, payload.source);
    return tr;
  }
  if (tr.hitUnit) applyShotToUnit(w, tr.hitUnit, payload);
  return tr;
}

function applyShotToUnit(w: World, u: Unit, payload: ShotPayload): void {
  if (payload.damage) w.damageUnit(u, payload.damage, payload.damageType!, payload.source, payload.armorIgnore ?? 0);
  if (u.dead) return;
  if (payload.corrode) w.applyStatus(u, 'corrode', payload.corrode, payload.source);
  if (payload.status) w.applyStatus(u, payload.status.id, payload.status.duration, payload.source);
  payload.onHitUnit?.(u);
}
