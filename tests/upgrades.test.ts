import { describe, expect, it } from 'vitest';
import { actionStatuses } from '../src/engine/commands';
import { loadoutProblems, newGame } from '../src/engine/step';
import { UP, type DeathHook } from '../src/rules/world';
import type { SourceRef } from '../src/state/types';
import { fireProjectile, shotPath } from '../src/rules/projectile';
import { C, eventsOf, inWorld, name, run, scenario, T, unitById } from './helpers';

const src: SourceRef = { side: 'player', category: 'player', kind: 'unit', id: 0, def: 'player', via: 'test' };
const pin = { remaining: 1, src };
const dummy = (at: string, extra: object = {}) => ({ def: 'turret', at, ammo: 0, ...extra });

describe('Tech upgrades', () => {
  it('T1 Piercing Rounds: Sidearm and turret kinetic damage ignores 2 armor', () => {
    const base = scenario({ player: 'B8', units: [{ def: 'warden', at: 'B5' }] });
    expect(unitById(run(base, { type: 'ability', ability: 'sidearm', target: 'B5' }).state, 1)!.hp).toBe(20);
    const s = scenario({ player: 'B8', upgrades: [UP.T1], units: [{ def: 'warden', at: 'B5' }, dummy('A7', { ammo: 4 })] });
    s.units[1]!.statuses.pinned = { ...pin };
    let r = run(s, { type: 'ability', ability: 'sidearm', target: 'B5' });
    expect(unitById(r.state, 1)!.hp).toBe(18);
    r = run(r.state, { type: 'endTurn' });
    const shot = eventsOf(r.events, 'DamageDealt').find((e) => e.source.via === 'turret_shot');
    expect(shot).toMatchObject({ target: { id: 1 }, amount: 2, absorbed: 0 });
  });

  it('T2 Shot-Triggered Mines: the Sidearm can target your mine, and a shot that reaches it detonates it', () => {
    const s = scenario({ player: 'B8', upgrades: [UP.T2], objects: [{ kind: 'mine', at: 'E5' }], units: [{ def: 'guard', at: 'E4' }] });
    const opts = actionStatuses(C, s).find((a) => a.id === 'sidearm')!.options.map((o) => name(o.target));
    expect(opts).toContain('E5');
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'E5' });
    expect(eventsOf(r.events, 'ObjectTriggered')).toHaveLength(1);
    expect(unitById(r.state, 1)!.hp).toBe(3);
    // Without the upgrade a mine is not a target, and other projectiles pass over mines.
    const plain = scenario({ player: 'B8', objects: [{ kind: 'mine', at: 'E5' }], units: [{ def: 'guard', at: 'E4' }] });
    expect(actionStatuses(C, plain).find((a) => a.id === 'sidearm')!.options.map((o) => name(o.target))).not.toContain('E5');
  });

  it('T3 Suppressing Fire: an enemy hit by a turret shot is Slowed for its next move', () => {
    const s = scenario({ player: 'A8', upgrades: [UP.T3], units: [dummy('C7', { ammo: 4 }), { def: 'guard', at: 'E4' }] });
    const r = run(s, { type: 'endTurn' });
    const slowed = eventsOf(r.events, 'StatusApplied').find((e) => e.status === 'slowed');
    expect(slowed?.target).toBe(2);
    // The slow lasts one round: it is gone after the environment phase.
    expect(unitById(r.state, 2)!.statuses.slowed).toBeUndefined();
  });

  it('T4 Grapple Pin: a pulled unit takes 1 kinetic on landing and is Pinned for its next phase', () => {
    const s = scenario({ player: 'B8', upgrades: [UP.T4], units: [{ def: 'guard', at: 'B5' }] });
    const r = run(s, { type: 'ability', ability: 'grapple_hook', target: 'B5', mode: 'unit' });
    const g = unitById(r.state, 1)!;
    expect(g.hp).toBe(5);
    expect(g.statuses.pinned?.remaining).toBe(1);
    const end = run(r.state, { type: 'endTurn' });
    expect(eventsOf(end.events, 'UnitMoved').some((e) => e.id === 1)).toBe(false);
  });

  it('T5 Reflective Shield: the first projectile each round is sent back along its line', () => {
    // The Guard on B5 shoots at the player on B8; the segment on B6 sends the shot back into the Guard.
    const s = scenario({ player: 'B8', upgrades: [UP.T5], objects: [{ kind: 'shield', at: 'B6' }], units: [{ def: 'guard', at: 'B5' }] });
    s.units[1]!.statuses.pinned = { ...pin };
    const r = run(s, { type: 'endTurn' });
    const shots = eventsOf(r.events, 'ProjectileFired').filter((e) => e.source.def === 'guard');
    expect(shots.map((e) => !!e.reflected)).toEqual([false, true]);
    expect(eventsOf(r.events, 'DamageDealt').filter((e) => e.source.def === 'guard')).toMatchObject([{ target: { kind: 'unit', id: 1 }, amount: 3 }]);
    expect(r.state.objects.find((o) => o.kind === 'shield')!.hp).toBe(4);
  });

  it('T5 Reflective Shield: a second projectile in the same round damages the segment', () => {
    const s = scenario({ player: 'B8', upgrades: [UP.T5], objects: [{ kind: 'shield', at: 'B6' }], units: [{ def: 'guard', at: 'B5' }] });
    const { events, s: after } = inWorld(s, (w) => {
      const g = w.unit(1)!;
      const payload = { source: w.unitSource(g, 'shot'), damage: 3, damageType: 'kinetic' as const, reflectable: true };
      fireProjectile(w, g.pos, shotPath(g.pos, T('B8')), payload);
      fireProjectile(w, g.pos, shotPath(g.pos, T('B8')), payload);
    });
    expect(eventsOf(events, 'ProjectileFired').map((e) => !!e.reflected)).toEqual([false, true, false]);
    expect(after.objects.find((o) => o.kind === 'shield')!.hp).toBe(1);
  });

  it('T6 Overcharged Drone: 3 charges, and the chain jumps one extra hop', () => {
    const s = scenario({ player: 'A8', upgrades: [UP.T6], ap: 3 });
    const r = run(s, { type: 'ability', ability: 'scout_drone', target: 'B8' });
    expect(r.state.units.find((u) => u.def === 'drone')!.charges).toBe(3);
    // The drone zaps the Medic on E7; the chain reaches F8 (adjacent to the Medic), and the extra hop reaches G7.
    const t = scenario({ player: 'A8', upgrades: [UP.T6], units: [{ def: 'drone', at: 'D8' }, { def: 'medic', at: 'E7' }, dummy('F8'), dummy('G7')] });
    t.units[2]!.statuses.pinned = { ...pin };
    const end = run(t, { type: 'endTurn' });
    const zapped = eventsOf(end.events, 'DamageDealt').filter((e) => e.source.via === 'zap').map((e) => e.target.id);
    expect(zapped).toEqual([2, 3, 4]);
  });
});

describe('Alien and hybrid upgrades', () => {
  it('A1 Spore Burst: when a poisoned unit dies, it poisons its 3x3 area', () => {
    const s = scenario({ player: 'E8', upgrades: [UP.A1], units: [{ def: 'guard', at: 'E3', hp: 2, statuses: { poison: { remaining: 3, src } } }, { def: 'medic', at: 'F3' }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'E3' });
    expect(eventsOf(r.events, 'AreaEffect').some((e) => e.kind === 'burst')).toBe(true);
    expect(unitById(r.state, 2)!.statuses.poison?.remaining).toBe(3);
  });

  it('A1 Spore Burst still fires when the poisoned unit was the last enemy, before the win is declared', () => {
    const s = scenario({ player: 'E8', wavesSpawned: 3, upgrades: [UP.A1], units: [{ def: 'guard', at: 'E3', hp: 2, statuses: { poison: { remaining: 3, src } } }, { def: 'hatchling', at: 'F3' }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'E3' });
    expect(r.state.outcome).toMatchObject({ result: 'win' });
    expect(eventsOf(r.events, 'AreaEffect').some((e) => e.kind === 'burst')).toBe(true);
    expect(unitById(r.state, 2)!.statuses.poison?.remaining).toBe(3);
    const t = r.events.map((e) => e.t);
    expect(t.lastIndexOf('AreaEffect')).toBeLessThan(t.indexOf('FightEnded'));
  });

  it('a queued death effect that kills the player still loses the fight, even after the last enemy fell', () => {
    // A death hook of the last enemy queues damage that kills a one-HP player. Loss takes priority over the win.
    const s = scenario({ player: 'E4', playerHp: 1, wavesSpawned: 3, units: [{ def: 'guard', at: 'E3', hp: 1 }] });
    const w = inWorld(s, (world) => {
      const guard = world.s.units.find((u) => u.def === 'guard')!;
      world.deathHooks.push((ww, dead) => {
        if (dead.id !== guard.id) return;
        ww.enqueue('test_blast', `u${dead.id}`, () => ww.damageUnit(ww.s.units[0]!, 5, 'kinetic', ww.playerEffect('test', 'unit', dead.id, dead.def)));
      });
      world.damageUnit(guard, 5, 'kinetic', world.playerEffect('test', 'unit', 0, 'player'));
    });
    expect(w.s.outcome).toMatchObject({ result: 'lose' });
  });

  it('A2 Acid Brood: when any hatchling dies, adjacent units gain 2 Corrode', () => {
    const s = scenario({ player: 'A8', upgrades: [UP.A2], units: [{ def: 'hatchling', at: 'E4', hp: 1 }, { def: 'warden', at: 'F4' }] });
    const { s: after } = inWorld(s, (w) => w.damageUnit(w.unit(1)!, 1, 'kinetic', w.unitSource(w.unit(2)!, 'melee')));
    expect(unitById(after, 2)!.statuses.corrode?.stacks).toBe(2);
  });

  it('A3 Parasite Jump: when a host dies, the Parasite jumps to the nearest enemy within 3 and sight', () => {
    const s = scenario({ player: 'E8', upgrades: [UP.A3], units: [{ def: 'guard', at: 'E5', hp: 2, statuses: { parasite: { remaining: 1, src } } }, { def: 'medic', at: 'G4' }, { def: 'guard', at: 'H1' }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'E5' });
    expect(unitById(r.state, 2)!.statuses.parasite?.remaining).toBe(3);
    expect(unitById(r.state, 3)!.statuses.parasite).toBeUndefined();
    // The host still bursts a hatchling.
    expect(eventsOf(r.events, 'UnitSpawned').map((e) => e.unit.def)).toEqual(['hatchling']);
  });

  it('A4 Fast Brood: corpse eggs hatch in 1 round', () => {
    const s = scenario({ player: 'B8', upgrades: [UP.A4], objects: [{ kind: 'corpse', at: 'C7', of: 'guard' }] });
    const r = run(s, { type: 'ability', ability: 'brood_egg', target: 'C7' });
    expect(r.state.objects.find((o) => o.kind === 'egg')!.timer).toBe(1);
  });

  it('H1 Spore Mines: mine explosions also poison every unit in the blast area', () => {
    const s = scenario({ player: 'A8', upgrades: [UP.H1], objects: [{ kind: 'mine', at: 'E7' }], units: [{ def: 'guard', at: 'E6' }, { def: 'medic', at: 'F8' }] });
    const { s: after } = inWorld(s, (w) => w.moveAlong(w.unit(1)!, [{ x: 4, y: 6 }], 'walk'));
    expect(unitById(after, 1)!.statuses.poison?.remaining).toBe(3);
    expect(unitById(after, 2)!.statuses.poison?.remaining).toBe(3);
  });

  it('H2 Plague Drone: zaps also poison the target and every unit the chain hits', () => {
    const s = scenario({ player: 'A8', upgrades: [UP.H2], units: [{ def: 'drone', at: 'D8' }, { def: 'medic', at: 'E6' }, dummy('E5')] });
    s.units[2]!.statuses.pinned = { ...pin };
    const r = run(s, { type: 'endTurn' });
    const poisoned = eventsOf(r.events, 'StatusApplied').filter((e) => e.status === 'poison' && !e.spread).map((e) => e.target);
    expect(poisoned).toEqual([2, 3]);
  });
});

describe('the once-per-chain rule', () => {
  it('suppresses a loop built from Spore Burst and Acid Brood', () => {
    const s = scenario({
      player: 'A8',
      upgrades: [UP.A1, UP.A2],
      units: [{ def: 'hatchling', at: 'E7', hp: 1, statuses: { poison: { remaining: 2, src } } }, { def: 'medic', at: 'F7' }],
    });
    const { events } = inWorld(s, (w) => {
      // A feedback hook that re-raises the death triggers of the unit that died, as a loop would.
      const echo: DeathHook = (w2, dead) => w2.enqueue('test_echo', `u${dead.id}`, () => w2.deathHooks.forEach((h) => h(w2, dead)));
      w.deathHooks.push(echo);
      w.damageUnit(w.unit(1)!, 1, 'kinetic', w.unitSource(w.unit(2)!, 'melee'));
    });
    expect(eventsOf(events, 'AreaEffect').filter((e) => e.kind === 'burst')).toHaveLength(1);
    expect(eventsOf(events, 'StatusApplied').filter((e) => e.status === 'corrode')).toHaveLength(1);
    expect(eventsOf(events, 'ChainSuppressed').map((e) => `${e.effect}@${e.source}:${e.reason}`)).toEqual([
      'A1_spore_burst@u1:repeat',
      'A2_acid_brood@u1:repeat',
      'test_echo@u1:repeat',
    ]);
  });

  it('stops a runaway chain at 20 trigger generations', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'medic', at: 'H1' }] });
    let n = 0;
    const { events } = inWorld(s, (w) => {
      const grow = () => {
        const id = n++;
        w.enqueue('test_grow', `g${id}`, grow);
      };
      grow();
    });
    expect(n).toBe(21);
    expect(eventsOf(events, 'ChainSuppressed')).toEqual([{ t: 'ChainSuppressed', effect: 'test_grow', source: 'g20', reason: 'depth' }]);
  });
});

describe('loadouts and presets', () => {
  it('every preset is a valid loadout and starts a fight', () => {
    for (const p of C.presets) {
      expect(loadoutProblems(C, { abilities: p.abilities, upgrades: p.upgrades }), p.id).toEqual([]);
      expect(newGame(C, { abilities: p.abilities, upgrades: p.upgrades }).state.units.length).toBe(5);
    }
  });

  it('an upgrade needs its required abilities', () => {
    expect(loadoutProblems(C, { abilities: ['sidearm'], upgrades: [UP.T2] })[0]).toMatch(/Shot-Triggered Mines needs/);
    expect(loadoutProblems(C, { abilities: ['auto_turret'], upgrades: [UP.T1] })).toEqual([]);
  });

  it('at most 8 abilities and 3 upgrades, no duplicates', () => {
    expect(loadoutProblems(C, { abilities: C.abilityOrder.slice(0, 9), upgrades: [] })).toContain('More than 8 abilities');
    expect(loadoutProblems(C, { abilities: ['sidearm', 'sidearm'], upgrades: [] })).toContain('Duplicate ability');
  });
});
