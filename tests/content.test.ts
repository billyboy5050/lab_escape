import { describe, expect, it } from 'vitest';
import { buildContent, defaultContentFiles, ContentError } from '../src/content';
import type { ContentFiles } from '../src/content/types';
import { validateContent } from '../src/content/load';

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
