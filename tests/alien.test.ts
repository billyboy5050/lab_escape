import { describe, expect, it } from 'vitest';
import { actionStatuses } from '../src/engine/commands';
import { step } from '../src/engine/step';
import type { SourceRef } from '../src/state/types';
import { C, contentWith, eventsOf, inWorld, name, run, scenario, unitAt, unitById } from './helpers';
import { spreadPoison, statusTickStep } from '../src/rules/environment';

const src: SourceRef = { side: 'player', category: 'player', kind: 'unit', id: 0, def: 'player', via: 'spore_pod' };
const pin = { remaining: 1, src };
/** Inert test subjects: empty turrets never act and never move. */
const dummy = (at: string, extra: object = {}) => ({ def: 'turret', at, ammo: 0, ...extra });

describe('Lunge', () => {
  it('leaps up to 3 tiles in a straight orthogonal line, over units, then may hit an adjacent unit for 2', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'C8' }, { def: 'guard', at: 'E7' }] });
    const opts = actionStatuses(C, s).find((a) => a.id === 'lunge')!.options;
    const lands = [...new Set(opts.map((o) => name(o.target)))];
    expect(lands.sort()).toEqual(['A8', 'B5', 'B6', 'B7', 'D8', 'E8'].sort());
    const r = run(s, { type: 'ability', ability: 'lunge', target: 'D8', target2: 'E7' });
    expect(name(unitById(r.state, 0)!.pos)).toBe('D8');
    expect(unitById(r.state, 2)!.hp).toBe(4);
  });

  it('cannot leap over pillars, walls or shields, and can be used purely to move', () => {
    const s = scenario({ player: 'B7', objects: [{ kind: 'shield', at: 'C7' }] });
    const lands = actionStatuses(C, s).find((a) => a.id === 'lunge')!.options.map((o) => name(o.target));
    expect(lands).not.toContain('D7');
    expect(lands).not.toContain('B4');
    expect(lands).toContain('B5');
    const r = run(s, { type: 'ability', ability: 'lunge', target: 'B5', target2: null });
    expect(name(unitById(r.state, 0)!.pos)).toBe('B5');
  });
});

describe('Spore Pod and poison', () => {
  it('poisons every unit in the 3x3 area, friend or foe, for 3 rounds', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'guard', at: 'D3' }, dummy('E4'), { def: 'medic', at: 'F5' }] });
    const r = run(s, { type: 'ability', ability: 'spore_pod', target: 'E4' });
    expect(eventsOf(r.events, 'StatusApplied').map((e) => [e.target, e.value])).toEqual([
      [1, 3],
      [2, 3],
      [3, 3],
    ]);
  });

  it('spreads to up to 3 new units a round across the whole board, in ID order at equal duration', () => {
    const s = scenario({ player: 'A8', units: [dummy('D7', { statuses: { poison: { remaining: 3, src } } }), dummy('C7'), dummy('E7'), dummy('C8'), dummy('D8'), dummy('E8')] });
    const { events } = inWorld(s, (w) => statusTickStep(w));
    const spread = eventsOf(events, 'StatusApplied').filter((e) => e.spread);
    expect(spread.map((e) => [e.target, e.value])).toEqual([
      [2, 2],
      [3, 2],
      [4, 2],
    ]);
  });

  it('infects in order of the duration a candidate would inherit, highest first', () => {
    const c = contentWith({ 'rules.statuses.poison.spreadCap': 1 });
    const s = scenario({
      content: c,
      player: 'H8',
      units: [dummy('A7', { statuses: { poison: { remaining: 1, src } } }), dummy('A8'), dummy('E7', { statuses: { poison: { remaining: 3, src } } }), dummy('E8')],
    });
    const { events } = inWorld(s, (w) => spreadPoison(w), c);
    expect(eventsOf(events, 'StatusApplied').map((e) => [e.target, e.value])).toEqual([[4, 3]]);
  });

  it('dies out in a cluster: direct targets take 3 ticks, the units they infect 2, the next ring 1', () => {
    let s = scenario({ player: 'H8', units: [dummy('A7', { statuses: { poison: { remaining: 3, src } } }), dummy('B7'), dummy('C7'), dummy('D7')] });
    const ticks = new Map<number, number>();
    for (let round = 0; round < 5; round++) {
      const r = run(s, { type: 'endTurn' });
      for (const e of eventsOf(r.events, 'DamageDealt')) if (e.source.via === 'poison') ticks.set(e.target.id, (ticks.get(e.target.id) ?? 0) + 1);
      s = r.state;
    }
    expect([...ticks.entries()].sort()).toEqual([
      [1, 3],
      [2, 2],
      [3, 1],
    ]);
    expect(s.units.every((u) => !u.statuses.poison)).toBe(true);
  });

  it('can spread to the player (friendly fire is universal)', () => {
    const s = scenario({ player: 'B8', units: [dummy('C8', { statuses: { poison: { remaining: 3, src } } })] });
    const { events } = inWorld(s, (w) => statusTickStep(w));
    expect(eventsOf(events, 'StatusApplied').map((e) => e.target)).toEqual([0]);
  });

  it('direct reapplication refreshes to 3', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'guard', at: 'D3', statuses: { poison: { remaining: 1, src } } }] });
    const r = run(s, { type: 'ability', ability: 'spore_pod', target: 'D3' });
    expect(unitById(r.state, 1)!.statuses.poison?.remaining).toBe(3);
  });
});

describe('Brood Egg', () => {
  it('a floor egg laid in round N hatches 2 hatchlings at the end of round N; they act in N+1', () => {
    let s = scenario({ player: 'B8', units: [{ def: 'medic', at: 'H1' }] });
    s = run(s, { type: 'ability', ability: 'brood_egg', target: 'D7' }).state;
    const egg = s.objects.find((o) => o.kind === 'egg')!;
    expect(egg).toMatchObject({ timer: 1, hp: 3, host: 'floor' });
    const r = run(s, { type: 'endTurn' });
    const h = eventsOf(r.events, 'EggHatched')[0]!;
    expect(h.spawned).toHaveLength(2);
    expect(eventsOf(r.events, 'UnitSpawned').map((e) => name(e.unit.pos))).toEqual(['D7', 'D6']);
    // They did not act in the round they hatched.
    expect(eventsOf(r.events, 'UnitMoved').some((e) => h.spawned.includes(e.id))).toBe(false);
  });

  it('a Guard corpse hosts a spitter in 2 rounds and is consumed; a Flamer corpse gives a burster', () => {
    let s = scenario({ player: 'B8', objects: [{ kind: 'corpse', at: 'C7', of: 'guard' }, { kind: 'corpse', at: 'E7', of: 'flamer' }], units: [{ def: 'medic', at: 'H1' }], ap: 6 });
    s = run(s, { type: 'ability', ability: 'brood_egg', target: 'C7' }).state;
    s = run(s, { type: 'ability', ability: 'brood_egg', target: 'E7' }).state;
    expect(s.objects.filter((o) => o.kind === 'corpse')).toHaveLength(0);
    expect(s.objects.filter((o) => o.kind === 'egg').map((o) => [o.hatchInto, o.timer])).toEqual([
      ['spitter', 2],
      ['burster', 2],
    ]);
    s = run(s, { type: 'endTurn' }).state;
    expect(s.objects.filter((o) => o.kind === 'egg').map((o) => o.timer)).toEqual([1, 1]);
    const r = run(s, { type: 'endTurn' });
    expect(eventsOf(r.events, 'UnitSpawned').map((e) => e.unit.def)).toEqual(['spitter', 'burster']);
  });

  it('a Medic or Warden corpse cannot host (the mender and armored hatchling are not built)', () => {
    const s = scenario({ player: 'B8', objects: [{ kind: 'corpse', at: 'C7', of: 'medic' }, { kind: 'corpse', at: 'B7', of: 'warden' }] });
    const tiles = actionStatuses(C, s).find((a) => a.id === 'brood_egg')!.options.map((o) => name(o.target));
    expect(tiles).not.toContain('C7');
    expect(tiles).not.toContain('B7');
  });

  it('an egg destroyed before it hatches produces nothing, and its corpse is lost', () => {
    const s = scenario({ player: 'B8', objects: [{ kind: 'egg', at: 'B5', timer: 2, host: 'guard', hatchInto: 'spitter' }] });
    const { s: after, events } = inWorld(s, (w) => w.damageObject(w.s.objects[0]!, 3, 'kinetic', w.playerEffect('test')));
    expect(eventsOf(events, 'ObjectDestroyed')).toHaveLength(1);
    expect(after.objects).toHaveLength(0);
  });

  it('cannot target a living unit', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'C7' }] });
    expect(step(C, s, { type: 'ability', ability: 'brood_egg', target: 'C7' }).ok).toBe(false);
  });
});

describe('Parasite', () => {
  it('deals 2 a round for 3 rounds, cannot stack, and reapplying restarts the 3 rounds', () => {
    let s = scenario({ player: 'B8', ap: 6, units: [{ def: 'warden', at: 'B5' }] });
    s.units[1]!.statuses.pinned = { ...pin };
    s = run(s, { type: 'ability', ability: 'parasite', target: 'B5' }).state;
    expect(unitById(s, 1)!.statuses.parasite?.remaining).toBe(3);
    const end = run(s, { type: 'endTurn' });
    const para = eventsOf(end.events, 'DamageDealt').filter((e) => e.source.via === 'parasite');
    expect(para.map((e) => e.amount)).toEqual([2]);
    expect(unitById(end.state, 1)!.statuses.parasite?.remaining).toBe(2);
    let t = end.state;
    t.units[1]!.statuses.pinned = { ...pin };
    t = run(t, { type: 'ability', ability: 'parasite', target: name(unitById(t, 1)!.pos) }).state;
    expect(unitById(t, 1)!.statuses.parasite?.remaining).toBe(3);
  });

  it('when the host dies, a basic hatchling spawns on its tile if the minion cap allows', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5', hp: 2, statuses: { parasite: { remaining: 3, src } } }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B5' });
    const spawn = eventsOf(r.events, 'UnitSpawned')[0]!;
    expect(spawn.unit.def).toBe('hatchling');
    expect(name(spawn.unit.pos)).toBe('B5');
    expect(spawn.unit.team).toBe('player');
  });

  it('a Parasite tick that kills spawns the hatchling after the tick step', () => {
    let s = scenario({ player: 'A8', units: [{ def: 'medic', at: 'H1', hp: 2, statuses: { parasite: { remaining: 3, src } } }] });
    const r = run(s, { type: 'endTurn' });
    s = r.state;
    expect(eventsOf(r.events, 'UnitDied').map((e) => e.def)).toEqual(['medic']);
    expect(unitAt(s, 'H1')?.def ?? s.units.find((u) => u.def === 'hatchling')?.def).toBe('hatchling');
  });
});

describe('hatchling behaviours', () => {
  it('a basic hatchling moves toward the nearest enemy and bites for 1', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'hatchling', at: 'D8' }, { def: 'medic', at: 'D5' }] });
    s.units[2]!.statuses.pinned = { ...pin };
    const r = run(s, { type: 'endTurn' });
    expect(eventsOf(r.events, 'DamageDealt').filter((e) => e.source.via === 'bite').map((e) => [e.target.id, e.amount])).toEqual([[2, 1]]);
  });

  it('a spitter moves until an enemy is within 3 with sight, then spits 1 acid', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'spitter', at: 'D8' }, { def: 'medic', at: 'E3' }] });
    s.units[2]!.statuses.pinned = { ...pin };
    const r = run(s, { type: 'endTurn' });
    const spit = eventsOf(r.events, 'DamageDealt').filter((e) => e.source.via === 'spit');
    expect(spit.map((e) => [e.target.id, e.amount, e.damageType])).toEqual([[2, 1, 'acid']]);
  });

  it('a burster detonates and dies when adjacent to an enemy: 3 explosive to its tile and neighbours', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'burster', at: 'D8' }, { def: 'guard', at: 'D5' }, { def: 'hatchling', at: 'H8' }] });
    s.units[2]!.statuses.pinned = { ...pin };
    const r = run(s, { type: 'endTurn' });
    const blast = eventsOf(r.events, 'DamageDealt').filter((e) => e.source.via === 'burst');
    expect(blast.some((e) => e.target.id === 2 && e.amount === 3)).toBe(true);
    expect(eventsOf(r.events, 'UnitDied').some((e) => e.def === 'burster')).toBe(true);
  });
});


