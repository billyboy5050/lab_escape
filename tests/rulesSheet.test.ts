import fs from 'node:fs';
import { describe, expect, it } from 'vitest';
import { C } from './helpers';

// The playtest rules sheet copies numbers and text from content/. Each copy is tagged with data-c="<path into the
// loaded content>" and each map cell with data-t="<map character>", so a content change that makes the printed sheet
// wrong fails here instead of reaching a tester.
const SHEET = fs.readFileSync('docs/rules-sheet.html', 'utf8');

function decode(text: string): string {
  return text.replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&amp;/g, '&');
}

/** Every tagged element on the sheet: its content path and its text. */
function tagged(): { path: string; text: string }[] {
  const out: { path: string; text: string }[] = [];
  for (const m of SHEET.matchAll(/<([a-z0-9]+)\b[^>]*\bdata-c="([^"]+)"[^>]*>(.*?)<\/\1>/gs)) out.push({ path: m[2]!, text: m[3]! });
  return out;
}

function lookup(path: string): unknown {
  let at: unknown = C;
  for (const key of path.split('.')) {
    if (at === null || typeof at !== 'object' || !(key in at)) return undefined;
    at = (at as Record<string, unknown>)[key];
  }
  return at;
}

/** How the sheet writes a content value: lists as "a, b and c". */
function written(value: unknown): string | undefined {
  if (typeof value === 'number' || typeof value === 'string') return String(value);
  if (Array.isArray(value) && value.length > 0 && value.every((v) => typeof v === 'string')) {
    return value.length === 1 ? value[0] : `${value.slice(0, -1).join(', ')} and ${value[value.length - 1]}`;
  }
  return undefined;
}

describe('rules sheet', () => {
  it('quotes every tagged value as content has it', () => {
    const tags = tagged();
    expect(tags.length).toBeGreaterThan(50);
    for (const { path, text } of tags) {
      expect(text, `${path} holds markup; tag the text itself`).not.toMatch(/</);
      const value = written(lookup(path));
      expect(value, `${path} is not a number, text or list of text in content`).toBeDefined();
      expect(decode(text), path).toBe(value);
    }
  });

  it('tags the numbers of every ability, enemy and lab defence', () => {
    const paths = new Set(tagged().map((t) => t.path));
    const want: string[] = ['units.player.hp', 'rules.turn.ap', 'rules.turn.movement', 'rules.sprint.ap', 'rules.sprint.movement', 'rules.caps.minions'];
    for (const id of C.abilityOrder) want.push(`abilities.${id}.name`, `abilities.${id}.ap`);
    for (const [id, u] of Object.entries(C.units)) if (u.team === 'enemy') want.push(`units.${id}.name`, `units.${id}.hp`, `units.${id}.armor`, `units.${id}.move`);
    for (const id of Object.keys(C.hazardDefs)) want.push(`hazardDefs.${id}.cue`);
    C.map.hazards.forEach((_, i) => want.push(`map.hazards.${i}.tiles`));
    for (const [id, u] of Object.entries(C.utilities)) if (u.requiresAbility) want.push(`utilities.${id}.ap`);
    expect(want.filter((p) => !paths.has(p))).toEqual([]);
  });

  it('draws the map from content', () => {
    const cells = [...SHEET.matchAll(/<td data-t="([^"]*)"/g)].map((m) => decode(m[1]!));
    const rows = Array.from({ length: C.map.height }, (_, y) => cells.slice(y * C.map.width, (y + 1) * C.map.width).join(''));
    expect(cells).toHaveLength(C.map.width * C.map.height);
    expect(rows).toEqual(C.map.rows);
  });

  it('matches the rule switches its wording assumes', () => {
    // "An enemy that arrived this round only moves": every wave has an arrival round.
    expect(C.rules.arrivalRound).toBe('all');
    // Corrode is "for the rest of the fight".
    expect(C.rules.statuses.corrode.duration).toBe(0);
    // "On a Guard corpse a spitter, on a Flamer corpse a burster"; "Medic and Warden corpses cannot hold one".
    expect(C.rules.eggs.corpseHosts).toEqual({ guard: 'spitter', flamer: 'burster', medic: null, warden: null });
  });
});
