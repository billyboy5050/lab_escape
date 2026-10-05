import { DIRS8, addPos, cheb } from '../state/grid';
import type { GameObject, Pos, SourceRef, StatusId, Unit } from '../state/types';
import type { World } from './world';

const TIMED: readonly StatusId[] = ['poison', 'parasite', 'pinned', 'slowed', 'corrode'];

/**
 * Environment step 3. Every status present at the start of the step ticks once, in unit ID order:
 * poison and Parasite deal damage, and every timed status counts down. Poison then spreads. Effects
 * set off by deaths during the step (Spore Burst, Parasite) run after the spread, so anything they
 * apply first ticks next round.
 */
export function statusTickStep(w: World): void {
  const r = w.c.rules.statuses;
  const order = w.activeUnits().sort((a, b) => a.id - b.id);
  const eligible = new Map<number, StatusId[]>(order.map((u) => [u.id, TIMED.filter((k) => u.statuses[k])]));
  for (const u of order) {
    if (u.dead) continue;
    const ks = eligible.get(u.id)!;
    // A unit that dies keeps its statuses, so death hooks (Spore Burst, Parasite) can see them.
    if (ks.includes('poison') && u.statuses.poison) {
      const st = u.statuses.poison;
      w.damageUnit(u, r.poison.damage, r.poison.damageType, tickSource(st.src, 'poison'));
      if (!u.dead) countDown(w, u, 'poison');
    }
    if (ks.includes('parasite') && u.statuses.parasite && !u.dead) {
      const st = u.statuses.parasite;
      w.damageUnit(u, r.parasite.damage, r.parasite.damageType, tickSource(st.src, 'parasite'));
      if (!u.dead) countDown(w, u, 'parasite');
    }
    if (!u.dead) {
      if (ks.includes('pinned')) countDown(w, u, 'pinned');
      if (ks.includes('slowed')) countDown(w, u, 'slowed');
      if (ks.includes('corrode') && u.statuses.corrode && u.statuses.corrode.remaining !== null) countDown(w, u, 'corrode');
    }
    w.flushDeaths();
  }
  spreadPoison(w);
}

function tickSource(src: SourceRef, via: string): SourceRef {
  return { ...src, via };
}

function countDown(w: World, u: Unit, id: StatusId): void {
  const st = u.statuses[id] as { remaining: number | null } | undefined;
  if (!st || st.remaining === null) return;
  st.remaining -= 1;
  w.emit({ t: 'StatusTick', target: u.id, status: id, remaining: st.remaining });
  if (st.remaining <= 0) {
    delete u.statuses[id];
    w.emit({ t: 'StatusExpired', target: u.id, status: id });
  }
}

/**
 * After the tick, every unpoisoned unit next to a poisoned one is a candidate. Candidates are infected in
 * order of the duration they would inherit (the best adjacent source's remaining rounds, highest first),
 * then by unit ID, up to the spread cap. A Warden that purged this round is immune.
 */
export function spreadPoison(w: World): void {
  const cap = w.c.rules.statuses.poison.spreadCap;
  const units = w.activeUnits();
  const sources = units.filter((u) => u.statuses.poison && u.statuses.poison.remaining > 0);
  if (!sources.length || cap <= 0) return;
  const cands: { u: Unit; d: number; src: SourceRef }[] = [];
  for (const u of units) {
    if (u.statuses.poison || u.purgeImmuneRound === w.s.round) continue;
    let best: Unit | null = null;
    for (const s of sources) {
      if (cheb(s.pos, u.pos) !== 1) continue;
      if (!best || s.statuses.poison!.remaining > best.statuses.poison!.remaining || (s.statuses.poison!.remaining === best.statuses.poison!.remaining && s.id < best.id)) best = s;
    }
    if (best) cands.push({ u, d: best.statuses.poison!.remaining, src: best.statuses.poison!.src });
  }
  cands.sort((a, b) => b.d - a.d || a.u.id - b.u.id);
  for (const c of cands.slice(0, cap)) w.applyStatus(c.u, 'poison', c.d, c.src, { spread: true });
}

/** Environment step 4: egg timers count down, and eggs at 0 hatch. */
export function eggStep(w: World): void {
  const eggs = w.s.objects.filter((o) => o.kind === 'egg' && !o.destroyed).sort((a, b) => a.id - b.id);
  for (const egg of eggs) {
    if (egg.destroyed) continue;
    egg.timer = (egg.timer ?? 1) - 1;
    if (egg.timer <= 0) hatchEgg(w, egg);
  }
}

/** Hatches an egg. Hatchlings that would exceed the minion cap do not spawn, but the egg still hatches. */
export function hatchEgg(w: World, egg: GameObject): void {
  w.removeObject(egg, 'hatched');
  const variant = egg.hatchInto!;
  const count = egg.host === 'floor' ? w.c.rules.eggs.floorHatchlings : 1;
  const spawned: Unit[] = [];
  let capped = 0;
  for (let i = 0; i < count; i++) {
    if (w.minionCount() >= w.c.rules.caps.minions) {
      capped++;
      continue;
    }
    const tile = i === 0 && w.isFree(egg.pos) ? egg.pos : nearestFreeAdjacent(w, egg.pos);
    if (!tile) {
      capped++;
      continue;
    }
    spawned.push(w.spawnUnit(variant, tile, 'egg', { triggerMines: false }));
  }
  w.emit({ t: 'EggHatched', id: egg.id, pos: { ...egg.pos }, host: egg.host ?? 'floor', spawned: spawned.map((u) => u.id), capped });
  for (const u of spawned) w.triggerMineUnder(u);
}

/** The first free tile among the 8 neighbours, lowest row then lowest column. */
export function nearestFreeAdjacent(w: World, p: Pos): Pos | null {
  for (const d of DIRS8) {
    const q = addPos(p, d);
    if (w.b.inBounds(q) && w.isFree(q)) return q;
  }
  return null;
}

/** Environment step 5: hazard cooldowns, unit cooldowns (Purge) and corpse decay advance. */
export function decayStep(w: World): void {
  for (const h of w.s.hazards) {
    if (h.state !== 'cooldown') continue;
    h.cooldown = Math.max(0, h.cooldown - 1);
    if (h.cooldown === 0) h.state = 'ready';
  }
  for (const u of w.s.units) {
    for (const k of Object.keys(u.cooldowns)) if (u.cooldowns[k]! > 0) u.cooldowns[k]! -= 1;
  }
  const corpses = w.s.objects.filter((o) => o.kind === 'corpse').sort((a, b) => a.id - b.id);
  for (const c of corpses) {
    c.decay = (c.decay ?? 1) - 1;
    if (c.decay <= 0) w.removeObject(c, 'decayed');
  }
}
