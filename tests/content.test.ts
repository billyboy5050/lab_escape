import { describe, expect, it } from 'vitest';
import { buildContent, defaultContentFiles, ContentError } from '../src/content';
import { validateContent } from '../src/content/load';
import { REQUIRED_ABILITY_FIELDS, REQUIRED_UNIT_FIELDS, REQUIRED_UPGRADE_FIELDS, REQUIRED_UTILITIES } from '../src/content/required';
import type { ContentFiles } from '../src/content/types';
import { actionStatuses } from '../src/engine/commands';
import { contentWith, run, scenario } from './helpers';

function edited(fn: (f: ContentFiles) => void): ContentFiles {
  const f = structuredClone(defaultContentFiles());
  fn(f);
  return f;
}

const ability = (f: ContentFiles, id: string) => f.abilities.abilities.find((a) => a.id === id)!;
const upgrade = (f: ContentFiles, id: string) => f.upgrades.find((u) => u.id === id)!;

describe('content validation of status blocks', () => {
  it('accepts the shipped content', () => {
    expect(validateContent(defaultContentFiles())).toEqual([]);
  });

  it.each(['spore_pod', 'parasite'])('rejects an edit that removes the status of %s', (id) => {
    const f = edited((x) => delete ability(x, id).status);
    expect(validateContent(f)).toEqual([`abilities.${id}.status is missing`]);
    expect(() => buildContent(f)).toThrow(ContentError);
  });

  it.each(['A1_spore_burst', 'H1_spore_mines', 'H2_plague_drone'])('rejects an edit that removes the status of upgrade %s', (id) => {
    const f = edited((x) => delete upgrade(x, id).status);
    expect(validateContent(f)).toEqual([`upgrades.${id}.status is missing`]);
  });

  it('rejects an unknown status id', () => {
    const f = edited((x) => (ability(x, 'spore_pod').status = { id: 'burning' as never, duration: 3 }));
    expect(validateContent(f)).toEqual(['abilities.spore_pod.status has unknown status "burning"']);
  });

  it.each([0, -1, 1.5, Number.NaN, '3'])('rejects a status duration of %s', (duration) => {
    const f = edited((x) => (ability(x, 'parasite').status = { id: 'parasite', duration: duration as never }));
    expect(validateContent(f)).toHaveLength(1);
    expect(validateContent(f)[0]).toContain('abilities.parasite.status.duration must be an integer >= 1');
  });

  it('checks the shape of an optional upgrade status that is present, but does not require one', () => {
    const bad = edited((x) => (upgrade(x, 'T4_grapple_pin').status = { id: 'pinned', duration: 0 }));
    expect(validateContent(bad)).toHaveLength(1);
    const none = edited((x) => delete upgrade(x, 'T4_grapple_pin').status);
    expect(validateContent(none)).toEqual([]);
  });

  it('does not throw on a non-object status', () => {
    const f = edited((x) => (ability(x, 'spore_pod').status = null as never));
    expect(validateContent(f).length).toBeGreaterThan(0);
  });
});

const drop = (o: object, path: string) => {
  const keys = path.split('.');
  const last = keys.pop()!;
  delete (keys.reduce((n, k) => (n as Record<string, unknown>)[k], o as unknown) as Record<string, unknown>)[last];
};
const pairs = (table: Record<string, readonly string[]>) => Object.entries(table).flatMap(([id, paths]) => paths.map((path) => [id, path] as const));

describe('content validation of required fields', () => {
  it.each(pairs(REQUIRED_ABILITY_FIELDS))('rejects abilities.%s without %s', (id, path) => {
    const f = edited((x) => drop(ability(x, id), path));
    expect(validateContent(f)).toContain(`abilities.${id}.${path} is missing`);
    expect(() => buildContent(f)).toThrow(ContentError);
  });

  it.each(pairs(REQUIRED_UNIT_FIELDS))('rejects units.%s without %s', (id, path) => {
    const f = edited((x) => drop(x.units.find((u) => u.id === id)!, path));
    expect(validateContent(f)).toContain(`units.${id}.${path} is missing`);
  });

  it.each(pairs(REQUIRED_UPGRADE_FIELDS))('rejects upgrades.%s without %s', (id, path) => {
    const f = edited((x) => drop(upgrade(x, id), path));
    expect(validateContent(f)).toContain(`upgrades.${id}.${path} is missing`);
  });

  it('rejects a proximity mine with no blast, a null blast, or a bad blast radius', () => {
    expect(validateContent(edited((x) => delete ability(x, 'proximity_mine').blast))).toEqual(['abilities.proximity_mine.blast is missing']);
    expect(validateContent(edited((x) => (ability(x, 'proximity_mine').blast = null as never)))).toContain('abilities.proximity_mine.blast is missing');
    const bad = validateContent(edited((x) => (ability(x, 'proximity_mine').blast!.radius = -1)));
    expect(bad).toEqual([expect.stringContaining('abilities.proximity_mine.blast.radius must be an integer >= 0')]);
  });

  it.each(REQUIRED_UTILITIES)('rejects content without the %s utility', (id) => {
    const f = edited((x) => (x.abilities.utilities = x.abilities.utilities.filter((u) => u.id !== id)));
    expect(validateContent(f)).toContain(`abilities.json is missing utility "${id}"`);
  });

  it('checks the numbers the AI does arithmetic on', () => {
    const f = edited((x) => {
      x.units.find((u) => u.id === 'flamer')!.sweep!.length = Number.NaN;
      x.units.find((u) => u.id === 'medic')!.heal!.amount = 0;
      x.units.find((u) => u.id === 'burster')!.blast!.radius = 1.5;
    });
    expect(validateContent(f)).toHaveLength(3);
  });
});

describe('sprint has one definition', () => {
  it('rejects content where the utility entry and rules.sprint disagree', () => {
    const ap = edited((x) => (x.rules.sprint.ap = 2));
    expect(validateContent(ap)).toEqual(['utilities.sprint.ap (1) must equal rules.sprint.ap (2)']);
    const mv = edited((x) => (x.rules.sprint.movement = 3));
    expect(validateContent(mv)).toEqual(['utilities.sprint.movement (2) must equal rules.sprint.movement (3)']);
  });

  it('offers, charges and shows the same cost when both are tuned together', () => {
    const c = contentWith({ 'rules.sprint.ap': 2, 'abilities.utilities.sprint.ap': 2 });
    const sprint = (ap: number) => actionStatuses(c, scenario({ ap, content: c })).find((a) => a.id === 'sprint')!;
    expect(sprint(1)).toMatchObject({ ap: 2, usable: false });
    expect(sprint(2)).toMatchObject({ ap: 2, usable: true });
    const s = scenario({ ap: 2, content: c });
    expect(run(s, { type: 'sprint' }, c).state.ap).toBe(0);
  });
});

describe('content validation of armorAppliesTo', () => {
  it.each([
    ['removed', (x: ContentFiles) => delete (x.rules as { armorAppliesTo?: unknown }).armorAppliesTo],
    ['null', (x: ContentFiles) => ((x.rules as { armorAppliesTo?: unknown }).armorAppliesTo = null)],
    ['not a list', (x: ContentFiles) => ((x.rules as { armorAppliesTo?: unknown }).armorAppliesTo = 'kinetic')],
  ])('rejects an edit that leaves it %s, since every damage calculation calls includes() on it', (_, edit) => {
    expect(validateContent(edited(edit))).toEqual(['rules.armorAppliesTo must be a list of damage types']);
  });

  it('still checks each entry, and accepts an empty list (armor then applies to nothing)', () => {
    expect(validateContent(edited((x) => (x.rules.armorAppliesTo = ['kinetic', 'fire'])))).toEqual([]);
    expect(validateContent(edited((x) => (x.rules.armorAppliesTo = [])))).toEqual([]);
    expect(validateContent(edited((x) => (x.rules.armorAppliesTo = ['kinetic', 'plasma' as never])))).toEqual(['rules.armorAppliesTo[1] has unknown damage type "plasma"']);
  });
});

describe('content validation of lane hazards', () => {
  const gun = (f: ContentFiles) => f.map.hazards.find((h) => h.id === 'gun')!;

  it.each(['Q', 'w', 'north', 5])('rejects a lane direction of %j, which would crash the first shot', (dir) => {
    const f = edited((x) => {
      (gun(x) as { direction?: unknown }).direction = dir;
    });
    expect(validateContent(f)).toEqual([`map hazard gun has unknown direction ${JSON.stringify(dir)} (use N, E, S or W)`]);
    expect(() => buildContent(f)).toThrow(ContentError);
  });

  it('still requires a direction on a lane', () => {
    const f = edited((x) => delete gun(x).direction);
    expect(validateContent(f)).toEqual(['map hazard gun is a lane and needs a direction']);
  });

  it.each(['N', 'E', 'S', 'W'] as const)('accepts %s', (dir) => {
    const f = edited((x) => {
      gun(x).direction = dir;
    });
    expect(validateContent(f)).toEqual([]);
  });
});

describe('content validation of hazard tiles', () => {
  it.each(['gun', 'panel', 'vent_b5'])('rejects a hazard with no tiles (%s), which would crash the board', (id) => {
    const f = edited((x) => {
      x.map.hazards.find((h) => h.id === id)!.tiles = [];
    });
    expect(validateContent(f)).toEqual([`map hazard ${id} needs at least one tile`]);
    expect(() => buildContent(f)).toThrow(ContentError);
  });

  it('rejects a hazard whose tiles are missing altogether, without throwing from the validator', () => {
    const f = edited((x) => {
      delete (x.map.hazards.find((h) => h.id === 'gun') as { tiles?: string[] }).tiles;
    });
    expect(validateContent(f)).toEqual(['map hazard gun needs at least one tile']);
  });

  it('still checks each tile that is there', () => {
    const f = edited((x) => {
      x.map.hazards.find((h) => h.id === 'panel')!.tiles = ['D5', 'Z9'];
    });
    expect(validateContent(f)).toEqual(['map hazard panel tile "Z9" is off the map']);
  });
});

describe('content validation of collections and blocks the game reads without a fallback', () => {
  const drop = (get: (f: ContentFiles) => object, key: string) => edited((x) => delete (get(x) as Record<string, unknown>)[key]);
  const setTo = (get: (f: ContentFiles) => object, key: string, v: unknown) =>
    edited((x) => {
      (get(x) as Record<string, unknown>)[key] = v;
    });

  it.each([
    ['map.hazards', drop((f) => f.map, 'hazards'), 'map.hazards must be a list'],
    ['hazards', edited((x) => ((x as { hazards: unknown }).hazards = undefined)), 'hazards must be a list'],
    ['presets', edited((x) => ((x as { presets: unknown }).presets = null)), 'presets must be a list'],
    ['intercom.lines', drop((f) => f.intercom, 'lines'), 'intercom.lines must be a list'],
    ['units', edited((x) => ((x as { units: unknown }).units = {})), 'units must be a list'],
    ['upgrades', edited((x) => ((x as { upgrades: unknown }).upgrades = 'none')), 'upgrades must be a list'],
    ['abilities.abilities', drop((f) => f.abilities, 'abilities'), 'abilities.abilities must be a list'],
    ['abilities.utilities', drop((f) => f.abilities, 'utilities'), 'abilities.utilities must be a list'],
  ])('rejects a missing or non-list %s, where it used to read as an empty list', (_name, f, problem) => {
    expect(validateContent(f)).toContain(problem);
    expect(() => buildContent(f)).toThrow(ContentError);
  });

  it('rejects map.hazards set to null as well as removed', () => {
    expect(validateContent(setTo((f) => f.map, 'hazards', null))).toEqual(['map.hazards must be a list']);
  });

  it('rejects an egg corpse-host table that is missing or not an object', () => {
    expect(validateContent(drop((f) => f.rules.eggs, 'corpseHosts'))).toEqual(['rules.eggs.corpseHosts must be an object']);
    expect(validateContent(setTo((f) => f.rules.eggs, 'corpseHosts', ['guard']))).toEqual(['rules.eggs.corpseHosts must be an object']);
  });

  describe('rules.ui', () => {
    it('rejects a removed block, which the board reads on every render', () => {
      const f = drop((x) => x.rules, 'ui');
      expect(validateContent(f)).toEqual(['rules.ui.showHazardCooldowns must be true or false', 'rules.ui.secondsPerActor must be a number of seconds, 0 or more (got undefined)']);
      expect(() => buildContent(f)).toThrow(ContentError);
    });

    it.each(['yes', 1, null])('rejects showHazardCooldowns of %j', (v) => {
      expect(validateContent(setTo((f) => f.rules.ui, 'showHazardCooldowns', v))).toEqual(['rules.ui.showHazardCooldowns must be true or false']);
    });

    it.each([-1, '0.4', null, NaN, Infinity])('rejects secondsPerActor of %j, which would make every animation delay invalid', (v) => {
      const problems = validateContent(setTo((f) => f.rules.ui, 'secondsPerActor', v));
      expect(problems).toHaveLength(1);
      expect(problems[0]).toMatch(/^rules\.ui\.secondsPerActor must be a number of seconds, 0 or more/);
    });

    it.each([0, 0.25, 2])('accepts secondsPerActor of %j', (v) => {
      expect(validateContent(setTo((f) => f.rules.ui, 'secondsPerActor', v))).toEqual([]);
    });
  });

  it('rejects an earlyWaveWhenEmpty that is not true or false', () => {
    expect(validateContent(setTo((f) => f.rules, 'earlyWaveWhenEmpty', 'yes'))).toEqual(['rules.earlyWaveWhenEmpty must be true or false']);
  });

  describe('upgrade requirements', () => {
    it('rejects an upgrade with no requires block, which the loadout screen asks of every upgrade', () => {
      const f = edited((x) => delete (upgrade(x, 'T1_piercing_rounds') as { requires?: unknown }).requires);
      expect(validateContent(f)).toEqual(['upgrades.T1_piercing_rounds.requires must be an object']);
    });

    it('rejects requirement lists that are not lists, and still names an unknown ability', () => {
      expect(validateContent(edited((x) => ((upgrade(x, 'T1_piercing_rounds').requires as { any: unknown }).any = 'sidearm')))).toEqual(['upgrades.T1_piercing_rounds.requires.any must be a list']);
      expect(validateContent(edited((x) => ((upgrade(x, 'T1_piercing_rounds').requires as { any: string[] }).any = ['sidearm', 'railgun'])))).toEqual(['upgrades.T1_piercing_rounds requires unknown ability "railgun"']);
    });
  });
});
