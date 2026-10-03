import type { Content } from '../content/types';
import { ABILITIES, type TargetOption } from '../effects/abilities';
import { DIRS4, addPos, cheb } from '../state/grid';
import type { Command, GameState, Pos, Unit } from '../state/types';
import { tileName } from '../util/tiles';
import { createWorld } from './phases';

export type ActionId = string;

export interface ActionStatus {
  /** Ability id, or a utility id: sprint, reload, pick_up_mine, redeploy. */
  id: ActionId;
  name: string;
  ap: number;
  kind: 'ability' | 'utility';
  usable: boolean;
  /** Why it cannot be used right now: not enough AP, no valid target, or a cap reached. */
  reason?: string;
  options: TargetOption[];
  /** Redeploy only: the docked drone it would place. */
  drone?: number;
}

/** Every action the player could take this phase, with its valid targets or the reason it is disabled. */
export function actionStatuses(c: Content, s: GameState): ActionStatus[] {
  const w = createWorld(c, s);
  const p = w.player;
  const out: ActionStatus[] = [];
  const phaseOk = s.phase === 'player' && !s.outcome && !!p;
  for (const id of s.loadout.abilities) {
    const def = c.abilities[id]!;
    const impl = ABILITIES[id]!;
    const st: ActionStatus = { id, name: def.name, ap: def.ap, kind: 'ability', usable: false, options: [] };
    if (!phaseOk) st.reason = 'Not your turn';
    else {
      const blocked = impl.blocked?.(w) ?? null;
      st.options = blocked ? [] : impl.options(w, p!);
      if (s.ap < def.ap) st.reason = `Not enough AP (needs ${def.ap})`;
      else if (blocked) st.reason = blocked;
      else if (!st.options.length) st.reason = 'No valid target';
      else st.usable = true;
    }
    out.push(st);
  }
  const util = (id: 'sprint' | 'reload' | 'pick_up_mine' | 'redeploy') => c.utilities[id]!;
  // Sprint
  {
    const u = util('sprint');
    const st: ActionStatus = { id: 'sprint', name: u.name, ap: u.ap, kind: 'utility', usable: false, options: [] };
    if (!phaseOk) st.reason = 'Not your turn';
    else if (s.ap < u.ap) st.reason = `Not enough AP (needs ${u.ap})`;
    else st.usable = true;
    out.push(st);
  }
  for (const id of ['reload', 'pick_up_mine', 'redeploy'] as const) {
    const u = util(id);
    if (u.requiresAbility && !s.loadout.abilities.includes(u.requiresAbility)) continue;
    const st: ActionStatus = { id, name: u.name, ap: u.ap, kind: 'utility', usable: false, options: [] };
    if (phaseOk) {
      if (id === 'reload') {
        st.options = w
          .playerSide()
          .filter((t) => t.maxAmmo !== undefined && (t.ammo ?? 0) < t.maxAmmo && cheb(t.pos, p!.pos) <= (u.range ?? 2))
          .map((t) => ({ target: { ...t.pos } }));
      } else if (id === 'pick_up_mine') {
        st.options = w.s.objects.filter((o) => o.kind === 'mine' && cheb(o.pos, p!.pos) <= (u.range ?? 1)).map((o) => ({ target: { ...o.pos } }));
      } else {
        const docked = s.units.find((x) => x.droneState === 'docked' && !x.dead);
        if (docked) {
          st.drone = docked.id;
          st.options = w.b.neighbors8(p!.pos).filter((t) => w.isFree(t)).map((t) => ({ target: t }));
        }
      }
    }
    if (!phaseOk) st.reason = 'Not your turn';
    else if (s.ap < u.ap) st.reason = `Not enough AP (needs ${u.ap})`;
    else if (!st.options.length) st.reason = id === 'redeploy' && st.drone === undefined ? 'No drone is docked' : 'No valid target';
    else st.usable = true;
    out.push(st);
  }
  return out;
}

export interface MoveOption {
  tile: Pos;
  path: Pos[];
  /** Mines the default path steps on (the path avoids them when it can). */
  mines: number;
}

/**
 * Tiles the player can reach with the movement left, each with a default path: fewest mines stepped on,
 * then fewest steps. Ground movement is 4-direction and cannot pass through units, shields, eggs or terrain.
 */
export function moveOptions(c: Content, s: GameState): MoveOption[] {
  if (s.phase !== 'player' || s.outcome) return [];
  const w = createWorld(c, s);
  const p = w.player;
  if (!p || s.movement <= 0) return [];
  const b = w.b;
  const n = b.width * b.height;
  const maxK = s.movement;
  // best[k][i] = fewest mines to stand on tile i after exactly k steps; parent pointers per layer.
  const best: Int16Array[] = [];
  const parent: Int16Array[] = [];
  for (let k = 0; k <= maxK; k++) {
    best.push(new Int16Array(n).fill(9999));
    parent.push(new Int16Array(n).fill(-1));
  }
  const free = (q: Pos) => w.isFree(q, p.id);
  best[0]![b.idx(p.pos)] = 0;
  for (let k = 0; k < maxK; k++) {
    for (let i = 0; i < n; i++) {
      const m = best[k]![i]!;
      if (m === 9999) continue;
      const from = b.pos(i);
      for (const d of DIRS4) {
        const q = addPos(from, d);
        if (!b.inBounds(q) || !free(q)) continue;
        const qi = b.idx(q);
        const cost = m + (w.mineAt(q) ? 1 : 0);
        if (cost < best[k + 1]![qi]!) {
          best[k + 1]![qi] = cost;
          parent[k + 1]![qi] = i;
        }
      }
    }
  }
  const out: MoveOption[] = [];
  for (let i = 0; i < n; i++) {
    if (i === b.idx(p.pos)) continue;
    let bestK = -1;
    for (let k = 1; k <= maxK; k++) {
      if (best[k]![i]! === 9999) continue;
      if (bestK < 0 || best[k]![i]! < best[bestK]![i]!) bestK = k;
    }
    if (bestK < 0) continue;
    const path: Pos[] = [];
    let cur = i;
    for (let k = bestK; k > 0; k--) {
      path.push(b.pos(cur));
      cur = parent[k]![cur]!;
    }
    path.reverse();
    out.push({ tile: b.pos(i), path, mines: best[bestK]![i]! });
  }
  return out;
}

/** Converts an action and target option into a command. */
export function commandFor(st: ActionStatus, opt: TargetOption): Command {
  switch (st.id) {
    case 'sprint':
      return { type: 'sprint' };
    case 'reload':
      return { type: 'reload', target: tileName(opt.target) };
    case 'pick_up_mine':
      return { type: 'pickUpMine', target: tileName(opt.target) };
    case 'redeploy':
      return { type: 'redeploy', drone: st.drone!, target: tileName(opt.target) };
    default: {
      const cmd: Command = { type: 'ability', ability: st.id, target: tileName(opt.target) };
      if (opt.target2) cmd.target2 = tileName(opt.target2);
      if (opt.mode) cmd.mode = opt.mode;
      return cmd;
    }
  }
}

export function moveCommand(path: Pos[]): Command {
  return { type: 'move', path: path.map(tileName) };
}

/** Every legal command in this state (used by bots). */
export function legalCommands(c: Content, s: GameState): Command[] {
  if (s.phase !== 'player' || s.outcome) return [];
  const out: Command[] = [];
  for (const m of moveOptions(c, s)) out.push(moveCommand(m.path));
  for (const st of actionStatuses(c, s)) {
    if (!st.usable) continue;
    if (st.id === 'sprint') out.push({ type: 'sprint' });
    else for (const opt of st.options) out.push(commandFor(st, opt));
  }
  out.push({ type: 'endTurn' });
  return out;
}

export function playerUnit(s: GameState): Unit | undefined {
  return s.units.find((u) => u.id === 0);
}
