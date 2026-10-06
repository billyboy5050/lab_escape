import { describe, expect, it } from 'vitest';
import { eventsOf, inWorld, scenario, T } from './helpers';

describe('deaths in the same flush as the player', () => {
  it('still reports the enemies that died together with the player', () => {
    const s = scenario({ player: 'D4', playerHp: 1, units: [{ def: 'guard', at: 'E4', hp: 1 }, { def: 'guard', at: 'D5', hp: 1 }] });
    const tiles = [T('D4'), T('E4'), T('D5')];
    const { s: after, events } = inWorld(s, (w) => {
      w.areaDamage(tiles, 50, 'explosive', w.playerEffect('test'));
    });
    expect(after.outcome?.result).toBe('lose');
    const died = eventsOf(events, 'UnitDied').map((e) => e.id);
    expect(died).toEqual([0, 1, 2]);
    expect(eventsOf(events, 'FightEnded')).toHaveLength(1);
    expect(after.units.some((u) => u.dead)).toBe(false);
  });
});
