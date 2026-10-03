import { describe, expect, it } from 'vitest';
import { actionStatuses } from '../src/engine/commands';
import { computeIntents } from '../src/preview/preview';
import type { GameState } from '../src/state/types';
import { step } from '../src/engine/step';
import { C, damageTo, eventsOf, name, run, scenario, unitAt, unitById } from './helpers';

const pin = { remaining: 1, src: { side: 'player' as const, category: 'player' as const, kind: 'unit' as const, id: 0, def: 'player', via: 'test' } };
const pinAll = (s: GameState) => {
  for (const u of s.units) if (u.team === 'enemy') u.statuses.pinned = { ...pin };
  return s;
};

describe('Proximity Mine', () => {
  it('places on an empty tile within 3, up to 4 at once', () => {
    let s = scenario({ player: 'D8', ap: 9, units: [{ def: 'medic', at: 'H1' }] });
    for (const t of ['A8', 'B7', 'D5', 'E6']) s = run(s, { type: 'ability', ability: 'proximity_mine', target: t }).state;
    expect(s.objects.filter((o) => o.kind === 'mine')).toHaveLength(4);
    const st = actionStatuses(C, s).find((a) => a.id === 'proximity_mine')!;
    expect(st.usable).toBe(false);
    expect(st.reason).toMatch(/Mine cap/);
  });

  it('any ground unit entering it sets it off: 3 explosive damage to its tile and the 8 neighbours', () => {
    const s = pinAll(scenario({ player: 'B8', objects: [{ kind: 'mine', at: 'B6' }], units: [{ def: 'guard', at: 'B3' }] }));
    const r = run(s, { type: 'move', path: ['B7', 'B6'] });
    expect(eventsOf(r.events, 'ObjectTriggered')).toHaveLength(1);
    expect(unitById(r.state, 0)!.hp).toBe(5);
    expect(r.state.objects.some((o) => o.kind === 'mine')).toBe(false);
    expect(r.commits).toBe(true);
  });

  it('flying units do not trigger mines', () => {
    const s = scenario({ player: 'A8', objects: [{ kind: 'mine', at: 'D7' }], units: [{ def: 'drone', at: 'C7' }, { def: 'medic', at: 'F7' }] });
    pinAll(s);
    const r = run(s, { type: 'endTurn' });
    expect(eventsOf(r.events, 'ObjectTriggered')).toHaveLength(0);
  });

  it('Pick Up removes an adjacent mine for 1 AP', () => {
    const s = scenario({ player: 'B8', objects: [{ kind: 'mine', at: 'C7' }] });
    const r = run(s, { type: 'pickUpMine', target: 'C7' });
    expect(r.state.objects).toHaveLength(0);
    expect(r.state.ap).toBe(2);
  });
});

describe('Auto-Turret and Reload', () => {
  it('shoots the nearest enemy within 5 and sight for 2, using 1 of 4 ammo; at 0 ammo it only blocks', () => {
    let s = scenario({ player: 'A8', units: [{ def: 'medic', at: 'E4' }] });
    s = run(s, { type: 'ability', ability: 'auto_turret', target: 'C7' }).state;
    const turret = s.units.find((u) => u.def === 'turret')!;
    pinAll(s);
    const r = run(s, { type: 'endTurn' });
    const shot = eventsOf(r.events, 'DamageDealt').find((e) => e.source.via === 'turret_shot')!;
    expect(shot.target.id).toBe(1);
    expect(shot.amount).toBe(2);
    expect(unitById(r.state, turret.id)!.ammo).toBe(3);
    const empty = structuredClone(r.state);
    unitById(empty, turret.id)!.ammo = 0;
    pinAll(empty);
    const r2 = run(empty, { type: 'endTurn' });
    expect(eventsOf(r2.events, 'DamageDealt').some((e) => e.source.via === 'turret_shot')).toBe(false);
  });

  it('Reload refills a turret within 2 for 1 AP', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'turret', at: 'C7', ammo: 1 }] });
    const r = run(s, { type: 'reload', target: 'C7' });
    expect(unitAt(r.state, 'C7')!.ammo).toBe(4);
    expect(r.state.ap).toBe(2);
  });
});

describe('Scout Drone cycle', () => {
  it('zaps twice, flies home, docks, and redeploys with full charges keeping its ID', () => {
    let s = scenario({ player: 'A8', units: [{ def: 'drone', at: 'D7' }, { def: 'medic', at: 'F7' }] });
    const charges: number[] = [];
    for (let i = 0; i < 2; i++) {
      pinAll(s);
      const r = run(s, { type: 'endTurn' });
      expect(eventsOf(r.events, 'ChainArc').length).toBeGreaterThan(0);
      s = r.state;
      charges.push(unitById(s, 1)!.charges!);
    }
    expect(charges).toEqual([1, 0]);
    expect(unitById(s, 1)!.droneState).toBe('returning');
    expect(unitById(s, 2)!.hp).toBe(3);
    pinAll(s);
    const r = run(s, { type: 'endTurn' });
    expect(eventsOf(r.events, 'DroneDocked')).toHaveLength(1);
    expect(eventsOf(r.events, 'ChainArc')).toHaveLength(0);
    s = r.state;
    expect(unitById(s, 1)!.droneState).toBe('docked');
    // Docked drones still count toward the minion cap.
    expect(s.units.filter((u) => u.kind === 'minion')).toHaveLength(1);
    const re = run(s, { type: 'redeploy', drone: 1, target: 'B8' });
    const d = unitById(re.state, 1)!;
    expect(d.droneState).toBe('deployed');
    expect(d.charges).toBe(2);
    expect(name(d.pos)).toBe('B8');
  });

  it('docks after 2 returning phases even when it cannot reach the player', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'drone', at: 'H1', charges: 0, droneState: 'returning' }, { def: 'medic', at: 'H8' }] });
    pinAll(s);
    let r = run(s, { type: 'endTurn' });
    expect(unitById(r.state, 1)!.droneState).toBe('returning');
    pinAll(r.state);
    r = run(r.state, { type: 'endTurn' });
    expect(unitById(r.state, 1)!.droneState).toBe('docked');
  });

  it('the chain hits every other unit adjacent to the target, friendly ones included, never the drone', () => {
    const s = pinAll(scenario({ player: 'A8', units: [{ def: 'drone', at: 'E7' }, { def: 'medic', at: 'F7' }, { def: 'hatchling', at: 'G8' }] }));
    const r = run(s, { type: 'endTurn' });
    const zap = eventsOf(r.events, 'DamageDealt').filter((e) => e.source.via === 'zap');
    expect(zap.map((e) => [e.target.id, e.amount])).toEqual([
      [2, 1],
      [3, 1],
    ]);
  });
});

describe('Barrier Shield', () => {
  it('places two segments of 4 HP on adjacent empty tiles within 4, at most 2 placements', () => {
    let s = scenario({ player: 'D8', ap: 9 });
    s = run(s, { type: 'ability', ability: 'barrier_shield', target: 'D6', target2: 'E6' }).state;
    s = run(s, { type: 'ability', ability: 'barrier_shield', target: 'D7', target2: 'C7' }).state;
    expect(s.objects.filter((o) => o.kind === 'shield').map((o) => o.hp)).toEqual([4, 4, 4, 4]);
    expect(actionStatuses(C, s).find((a) => a.id === 'barrier_shield')!.reason).toMatch(/Shield cap/);
  });

  it('blocks movement and projectiles but not sight', () => {
    const s = scenario({ player: 'B8', objects: [{ kind: 'shield', at: 'B7' }], units: [{ def: 'guard', at: 'B5' }] });
    expect(step(C, s, { type: 'move', path: ['B7'] }).ok).toBe(false);
    // Sight passes the shield, so the Guard can be targeted, but the shot stops in the segment.
    expect(actionStatuses(C, s).find((a) => a.id === 'sidearm')!.options.map((o) => name(o.target))).toEqual(['B5']);
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B5' });
    expect(eventsOf(r.events, 'DamageDealt')[0]!.target.kind).toBe('object');
  });
});

describe('Grapple Hook', () => {
  it('pull unit: drags the first unit hit until adjacent to the player', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }] });
    const r = run(s, { type: 'ability', ability: 'grapple_hook', target: 'B5', mode: 'unit' });
    expect(name(unitById(r.state, 1)!.pos)).toBe('B7');
  });

  it('pull self: travels toward the tile and stops on the last free tile before a blocker', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }] });
    const r = run(s, { type: 'ability', ability: 'grapple_hook', target: 'B5', mode: 'self' });
    expect(name(unitById(r.state, 0)!.pos)).toBe('B6');
  });

  it('cannot pull the Warden', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'warden', at: 'B5' }] });
    const opts = actionStatuses(C, s).find((a) => a.id === 'grapple_hook')!.options;
    expect(opts.some((o) => o.mode === 'unit')).toBe(false);
  });

  it('a pulled ground unit sets off a mine on the way', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }], objects: [{ kind: 'mine', at: 'B6' }] });
    const r = run(s, { type: 'ability', ability: 'grapple_hook', target: 'B5', mode: 'unit' });
    expect(eventsOf(r.events, 'ObjectTriggered')).toHaveLength(1);
    expect(unitById(r.state, 1)!.hp).toBe(3);
    expect(unitById(r.state, 0)!.hp).toBe(8);
  });
});

describe('minion cap', () => {
  it('blocks summons at 5 minions, including a docked drone', () => {
    const s = scenario({
      player: 'D8',
      units: [{ def: 'turret', at: 'A8' }, { def: 'turret', at: 'B8' }, { def: 'hatchling', at: 'H8' }, { def: 'hatchling', at: 'H7' }, { def: 'drone', at: 'H6', droneState: 'docked' }],
    });
    const st = actionStatuses(C, s);
    expect(st.find((a) => a.id === 'auto_turret')!.reason).toMatch(/Minion cap/);
    expect(st.find((a) => a.id === 'scout_drone')!.reason).toMatch(/Minion cap/);
  });

  it('an egg still hatches at the cap, but hatchlings over the cap do not spawn', () => {
    const s = scenario({
      player: 'D8',
      units: [{ def: 'turret', at: 'A8' }, { def: 'turret', at: 'B8' }, { def: 'turret', at: 'H8' }, { def: 'turret', at: 'H7' }],
      objects: [{ kind: 'egg', at: 'E7', timer: 1 }],
    });
    const r = run(s, { type: 'endTurn' });
    const h = eventsOf(r.events, 'EggHatched')[0]!;
    expect(h.spawned).toHaveLength(1);
    expect(h.capped).toBe(1);
  });
});

describe('intents', () => {
  it('shows each minion and enemy plan, recomputed from the current state', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'turret', at: 'C7' }, { def: 'guard', at: 'E4' }] });
    const rep = computeIntents(C, s);
    const turret = rep.intents.find((i) => i.def === 'turret')!;
    expect(turret.hits.map((h) => [h.ref.id, h.damage])).toEqual([[2, 2]]);
    // The Guard shoots the nearest player-side unit it has a clear shot at: the turret.
    const guard = rep.intents.find((i) => i.def === 'guard')!;
    expect(guard.hits.map((h) => [h.ref.id, h.damage])).toEqual([[1, 3]]);
    expect(rep.playerDamage).toBe(0);
    // The real end of turn matches the intents.
    const r = run(s, { type: 'endTurn' });
    expect(damageTo(r.events, 2)).toBe(2);
    expect(damageTo(r.events, 1)).toBe(3);
  });

  it('the unit whose attack would kill the player keeps its intent, and later units are marked as not acting', () => {
    const s = scenario({ player: 'B8', playerHp: 6, units: [{ def: 'guard', at: 'B6' }, { def: 'guard', at: 'C7' }, { def: 'medic', at: 'H1' }] });
    const rep = computeIntents(C, s);
    expect(rep.playerDies).toBe(true);
    const killer = rep.intents.find((i) => i.id === 2)!;
    expect(killer.hits.map((h) => [h.ref.id, h.damage, h.killed])).toEqual([[0, 3, true]]);
    expect(rep.intents.find((i) => i.id === 3)!.label).toMatch(/fight ends first/);
  });

  it('a unit in its arrival round shows its destination only', () => {
    const s = scenario({ player: 'E8', units: [{ def: 'guard', at: 'A1', arrival: true }] });
    const rep = computeIntents(C, s);
    expect(rep.intents[0]!.arrival).toBe(true);
    expect(rep.intents[0]!.hits).toHaveLength(0);
    expect(name(rep.intents[0]!.to)).toBe('B3');
  });
});
