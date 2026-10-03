import { describe, expect, it } from 'vitest';
import { summarize } from '../src/telemetry/summary';
import { damageTo, eventsOf, inWorld, run, scenario, unitById } from './helpers';

describe('armor and Corrode', () => {
  const warden = () => scenario({ player: 'B8', units: [{ def: 'warden', at: 'B5' }] });

  it('armor subtracts from kinetic, explosive and fire damage, to a minimum of 0', () => {
    const s = warden();
    const { events } = inWorld(s, (w) => {
      const wd = w.unit(1)!;
      w.damageUnit(wd, 2, 'kinetic', w.unitSource(w.player!, 'sidearm'));
      w.damageUnit(wd, 3, 'explosive', w.unitSource(w.player!, 'mine'));
      w.damageUnit(wd, 3, 'fire', w.unitSource(w.player!, 'test'));
    });
    const dd = eventsOf(events, 'DamageDealt');
    expect(dd.map((e) => [e.amount, e.absorbed])).toEqual([
      [0, 2],
      [1, 2],
      [1, 2],
    ]);
  });

  it('electric, acid, poison and Parasite damage ignore armor', () => {
    const s = warden();
    const { events } = inWorld(s, (w) => {
      const wd = w.unit(1)!;
      for (const t of ['electric', 'acid', 'poison', 'parasite'] as const) w.damageUnit(wd, 2, t, w.unitSource(w.player!, 'test'));
    });
    expect(eventsOf(events, 'DamageDealt').map((e) => e.amount)).toEqual([2, 2, 2, 2]);
  });

  it('each Corrode stack removes 1 armor, down to 0', () => {
    const s = warden();
    const { events } = inWorld(s, (w) => {
      const wd = w.unit(1)!;
      w.applyStatus(wd, 'corrode', 1, w.unitSource(w.player!, 'acid_spit'));
      w.damageUnit(wd, 2, 'kinetic', w.unitSource(w.player!, 'sidearm'));
      w.applyStatus(wd, 'corrode', 5, w.unitSource(w.player!, 'acid_spit'));
      w.damageUnit(wd, 2, 'kinetic', w.unitSource(w.player!, 'sidearm'));
      expect(w.armorOf(wd)).toBe(0);
    });
    expect(eventsOf(events, 'DamageDealt').map((e) => e.amount)).toEqual([1, 2]);
  });

  it('one Acid Spit removes the Warden armor for the rest of the fight', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'warden', at: 'B5' }], ap: 3 });
    let r = run(s, { type: 'ability', ability: 'acid_spit', target: 'B5' });
    const wd = unitById(r.state, 1)!;
    expect(wd.hp).toBe(19);
    expect(wd.statuses.corrode?.stacks).toBe(2);
    expect(wd.statuses.corrode?.remaining).toBeNull();
    r = run(r.state, { type: 'ability', ability: 'sidearm', target: 'B5' });
    expect(unitById(r.state, 1)!.hp).toBe(17);
  });

  it('healing cannot exceed maximum HP', () => {
    const s = scenario({ units: [{ def: 'guard', at: 'E3', hp: 5 }] });
    const { events } = inWorld(s, (w) => {
      w.heal(w.unit(1)!, 2, w.unitSource(w.unit(1)!, 'heal'));
    });
    expect(eventsOf(events, 'Healed')[0]!.amount).toBe(1);
  });
});

describe('projectiles and friendly fire', () => {
  it('a projectile hits the first unit on its line, friendly or not', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }, { def: 'hatchling', at: 'B6' }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B5' });
    expect(damageTo(r.events, 2)).toBe(2);
    expect(damageTo(r.events, 1)).toBe(0);
  });

  it('a mine hurts your own minion (friendly fire)', () => {
    const s = scenario({ player: 'B8', objects: [{ kind: 'mine', at: 'D6' }], units: [{ def: 'turret', at: 'E6' }, { def: 'guard', at: 'D4' }] });
    // The Guard walks onto the mine while closing in; the turret next to it takes the blast.
    const { events } = inWorld(s, (w) => {
      w.moveAlong(w.unit(2)!, [{ x: 3, y: 4 }, { x: 3, y: 5 }], 'walk');
    });
    expect(eventsOf(events, 'ObjectTriggered')).toHaveLength(1);
    const toTurret = eventsOf(events, 'DamageDealt').find((e) => e.target.id === 1)!;
    expect(toTurret.amount).toBe(3);
    expect(toTurret.source.side).toBe('player');
  });

  it('shield segments and eggs stop projectiles; the shutter and wall gun do too', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }], objects: [{ kind: 'shield', at: 'B6' }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B5' });
    const dd = eventsOf(r.events, 'DamageDealt');
    expect(dd).toHaveLength(1);
    expect(dd[0]!.target.kind).toBe('object');
    expect(r.state.objects.find((o) => o.kind === 'shield')!.hp).toBe(2);
  });

  it('projectiles pass over mines and corpses', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B5' }], objects: [{ kind: 'mine', at: 'B7' }, { kind: 'corpse', at: 'B6' }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B5' });
    expect(damageTo(r.events, 1)).toBe(2);
    expect(r.state.objects.some((o) => o.kind === 'mine')).toBe(true);
  });
});

describe('death, corpses and the end of the fight', () => {
  it('an enemy leaves a corpse; player-side units leave none', () => {
    const s = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B6', hp: 2 }, { def: 'hatchling', at: 'D6', hp: 1 }] });
    const { s: after } = inWorld(s, (w) => {
      w.damageUnit(w.unit(1)!, 5, 'acid', w.unitSource(w.player!, 'test'));
      w.damageUnit(w.unit(2)!, 5, 'acid', w.unitSource(w.player!, 'test'));
    });
    expect(after.objects.filter((o) => o.kind === 'corpse').map((o) => o.of)).toEqual(['guard']);
  });

  it('area damage hits every target first, then deaths are processed in unit ID order', () => {
    const s = scenario({ player: 'A8', units: [{ def: 'hatchling', at: 'D5' }, { def: 'guard', at: 'E5', hp: 1 }, { def: 'hatchling', at: 'D6' }] });
    const { events } = inWorld(s, (w) => {
      w.areaDamage(w.b.square({ x: 3, y: 4 }, 1), 3, 'explosive', w.playerEffect('test'));
    });
    const kinds = events.map((e) => e.t).filter((t) => t === 'DamageDealt' || t === 'UnitDied');
    expect(kinds).toEqual(['DamageDealt', 'DamageDealt', 'DamageDealt', 'UnitDied', 'UnitDied', 'UnitDied']);
    expect(eventsOf(events, 'UnitDied').map((e) => e.id)).toEqual([1, 2, 3]);
  });

  it('summaries count HP actually lost, not overkill', () => {
    const s = scenario({ player: 'B8', playerHp: 2, units: [{ def: 'guard', at: 'B6' }] });
    const r = run(s, { type: 'endTurn' });
    expect(eventsOf(r.events, 'DamageDealt')[0]).toMatchObject({ amount: 3, hpLost: 2, killed: true });
    expect(summarize(r.events, r.state).playerDamageTaken.enemy).toBe(2);
  });

  it('the player dying is an immediate loss', () => {
    const s = scenario({ player: 'B8', playerHp: 3, units: [{ def: 'guard', at: 'B6' }, { def: 'guard', at: 'C7' }] });
    const r = run(s, { type: 'endTurn' });
    expect(r.state.outcome?.result).toBe('lose');
    expect(r.state.outcome?.cause).toContain('guard');
    // The second Guard never acts.
    expect(eventsOf(r.events, 'ProjectileFired')).toHaveLength(1);
  });

  it('the fight is won when the last wave has spawned and every enemy is dead', () => {
    const s = scenario({ player: 'B8', wavesSpawned: 3, units: [{ def: 'guard', at: 'B6', hp: 2 }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B6' });
    expect(r.state.outcome?.result).toBe('win');
  });

  it('killing everything before the last wave is not a win', () => {
    const s = scenario({ player: 'B8', wavesSpawned: 2, units: [{ def: 'guard', at: 'B6', hp: 2 }] });
    const r = run(s, { type: 'ability', ability: 'sidearm', target: 'B6' });
    expect(r.state.outcome).toBeNull();
  });

  it('round 30 ending unresolved is a loss', () => {
    const s = scenario({ player: 'A8', round: 30, wavesSpawned: 3, units: [{ def: 'guard', at: 'H1' }], objects: [] });
    // Wall the Guard off so the round passes without anyone dying.
    s.units[0]!.pos = { x: 0, y: 7 };
    s.objects.push(
      { id: 90, kind: 'shield', pos: { x: 6, y: 0 }, team: 'player', hp: 99, maxHp: 99, placement: 1, createdRound: 30 },
      { id: 91, kind: 'shield', pos: { x: 6, y: 1 }, team: 'player', hp: 99, maxHp: 99, placement: 1, createdRound: 30 },
      { id: 92, kind: 'shield', pos: { x: 7, y: 2 }, team: 'player', hp: 99, maxHp: 99, placement: 2, createdRound: 30 },
    );
    const r = run(s, { type: 'endTurn' });
    expect(r.state.outcome?.result).toBe('lose');
    expect(r.state.outcome?.cause).toMatch(/Round 30/);
  });
});

describe('timers tick in the round they start', () => {
  it('poison applied in round N deals damage in rounds N, N+1 and N+2, then ends', () => {
    let s = scenario({ player: 'A8', units: [{ def: 'guard', at: 'H1', hp: 6 }], wavesSpawned: 2 });
    s.units[1]!.statuses.poison = { remaining: 3, src: { side: 'player', category: 'player', kind: 'unit', id: 0, def: 'player', via: 'spore_pod' } };
    // Keep the Guard out of the fight: box it into the corner with shields.
    s.objects.push(
      { id: 90, kind: 'shield', pos: { x: 6, y: 0 }, team: 'player', hp: 99, maxHp: 99, placement: 1, createdRound: 2 },
      { id: 91, kind: 'shield', pos: { x: 6, y: 1 }, team: 'player', hp: 99, maxHp: 99, placement: 1, createdRound: 2 },
    );
    const hp: number[] = [];
    for (let i = 0; i < 4; i++) {
      s = run(s, { type: 'endTurn' }).state;
      hp.push(unitById(s, 1)!.hp);
    }
    expect(hp).toEqual([5, 4, 3, 3]);
    expect(unitById(s, 1)!.statuses.poison).toBeUndefined();
  });
});

describe('corpse decay', () => {
  it('lasts the whole next player phase, then decays at step 5', () => {
    const s0 = scenario({ player: 'B8', units: [{ def: 'guard', at: 'B6', hp: 2 }, { def: 'medic', at: 'H1' }] });
    let s = run(s0, { type: 'ability', ability: 'sidearm', target: 'B6' }).state;
    expect(s.objects.some((o) => o.kind === 'corpse')).toBe(true);
    s = run(s, { type: 'endTurn' }).state; // round N ends: decay 2 -> 1
    expect(s.objects.find((o) => o.kind === 'corpse')?.decay).toBe(1);
    expect(s.round).toBe(3);
    s = run(s, { type: 'endTurn' }).state; // round N+1 ends: removed
    expect(s.objects.some((o) => o.kind === 'corpse')).toBe(false);
  });
});
