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
