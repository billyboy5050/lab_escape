import { cheb, samePos } from '../state/grid';
import type { Unit } from '../state/types';
import { minBy } from '../rules/pathing';
import { UP, type DeathHook, type World } from '../rules/world';

/**
 * Death hooks, registered in a fixed order: upgrade hooks in upgrade-table order, then the base
 * Parasite rule. A hatchling bursting from a host therefore appears after the host's Spore Burst,
 * so the burst does not poison it.
 */
export function registerHooks(w: World): void {
  if (w.hasUp('A1')) w.deathHooks.push(sporeBurst);
  if (w.hasUp('A2')) w.deathHooks.push(acidBrood);
  if (w.hasUp('A3')) w.deathHooks.push(parasiteJump);
  w.deathHooks.push(parasiteHatch);
}

/** A1 Spore Burst: when a poisoned unit dies, it poisons its 3x3 area. */
export const sporeBurst: DeathHook = (w, dead) => {
  if (!dead.statuses.poison) return;
  const pos = { ...dead.pos };
  w.enqueue(UP.A1, `u${dead.id}`, () => {
    const up = w.up('A1');
    const tiles = w.b.square(pos, up.area!.radius);
    const src = w.playerEffect(UP.A1, 'unit', dead.id, dead.def);
    w.emit({ t: 'AreaEffect', kind: 'burst', tiles, source: src });
    for (const u of unitsOn(w, tiles)) w.applyStatus(u, up.status!.id, up.status!.duration, src);
  });
};

/** A2 Acid Brood: when any hatchling dies, adjacent units gain Corrode. */
export const acidBrood: DeathHook = (w, dead) => {
  if (!w.c.units[dead.def]?.hatchling) return;
  const pos = { ...dead.pos };
  w.enqueue(UP.A2, `u${dead.id}`, () => {
    const up = w.up('A2');
    const src = w.playerEffect(UP.A2, 'unit', dead.id, dead.def);
    for (const u of w.activeUnits().filter((x) => cheb(x.pos, pos) === 1).sort((a, b) => a.id - b.id)) {
      w.applyStatus(u, 'corrode', up.corrode ?? 0, src);
    }
  });
};

/** A3 Parasite Jump: when a Parasite host dies, the Parasite jumps to the nearest enemy within range and sight. */
export const parasiteJump: DeathHook = (w, dead) => {
  const para = dead.statuses.parasite;
  if (!para) return;
  const pos = { ...dead.pos };
  w.enqueue(UP.A3, `u${dead.id}`, () => {
    const up = w.up('A3');
    const range = up.range!;
    const cands = w.enemies().filter((u) => u.id !== dead.id && cheb(u.pos, pos) <= range && w.b.hasLOS(pos, u.pos));
    const target = minBy(cands, (u) => [cheb(u.pos, pos), u.hp, u.id]);
    if (target) w.applyStatus(target, 'parasite', up.status?.duration ?? w.c.rules.statuses.parasite.duration, { ...para.src, via: UP.A3 });
  });
};

/** Base rule: when a Parasite host dies, a basic hatchling spawns on its tile if the minion cap allows. */
export const parasiteHatch: DeathHook = (w, dead) => {
  if (!dead.statuses.parasite) return;
  const pos = { ...dead.pos };
  w.enqueue('parasite_hatch', `u${dead.id}`, () => {
    if (w.minionCount() >= w.c.rules.caps.minions) return;
    const tile = w.isFree(pos) ? pos : w.findSpawnTile(pos);
    if (tile) w.spawnUnit(w.c.rules.parasiteHatchling, tile, 'parasite');
  });
};

function unitsOn(w: World, tiles: { x: number; y: number }[]): Unit[] {
  return w.activeUnits()
    .filter((u) => tiles.some((t) => samePos(t, u.pos)))
    .sort((a, b) => a.id - b.id);
}
