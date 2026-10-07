import fs from 'node:fs';
import { describe, expect, it } from 'vitest';
import { tileName } from '../src/util/tiles';
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

/**
 * The sheet's text with its head, map, key and glyph examples, and every tagged value taken out. Whatever number or
 * damage type is left was typed in by hand, so a tag dropped from a copied value shows up here.
 */
function untaggedText(): string {
  const html = SHEET.replace(/<head>.*<\/head>/s, ' ')
    .replace(/<table class="map".*?<\/table>/s, ' ')
    .replace(/<([a-z0-9]+)\b[^>]*\bdata-c="[^"]+"[^>]*>.*?<\/\1>/gs, ' ')
    .replace(/<kbd>.*?<\/kbd>/gs, ' ')
    .replace(/<span class="(?:g|k\b[^"]*)">.*?<\/span>/gs, ' ')
    .replace(/<[^>]+>/g, ' ');
  return decode(html).replace(/\s+/g, ' ');
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

  it('leaves no number or damage type untagged', () => {
    // Fixed by the rules, not by content: losing at 0 HP, range counting a diagonal as 1, the 8 tiles around a tile
    // (radius 1, checked below), a 3x3 area (the same) and Corrode's 1 armor per stack. Armor's complement is checked
    // below too.
    const fixed = ['at 0 HP', 'count as 1', 'the 8 tiles around', 'the 8 around', '3x3', '1 less armor', 'Electric, acid, poison and Parasite damage ignore it'];
    let text = untaggedText();
    for (const phrase of fixed) {
      expect(text, `"${phrase}" is no longer on the sheet; drop it from this list`).toContain(phrase);
      text = text.split(phrase).join(' ');
    }
    expect(text.match(/\S*\d\S*/g) ?? [], 'numbers on the sheet without a data-c tag').toEqual([]);
    expect(text.match(/\b(?:kinetic|explosive|fire|electric|acid)\b/g) ?? [], 'damage types without a data-c tag').toEqual([]);
  });

  it('draws the map from content', () => {
    const cells = [...SHEET.matchAll(/<td data-t="([^"]*)"/g)].map((m) => decode(m[1]!));
    const rows = Array.from({ length: C.map.height }, (_, y) => cells.slice(y * C.map.width, (y + 1) * C.map.width).join(''));
    expect(cells).toHaveLength(C.map.width * C.map.height);
    expect(rows).toEqual(C.map.rows);
    // The @ marks "Your start", but the player is placed from map.playerStart.
    const y = C.map.rows.findIndex((r) => r.includes('@'));
    expect(y, 'the map has no @').toBeGreaterThanOrEqual(0);
    expect(tileName({ x: C.map.rows[y]!.indexOf('@'), y })).toBe(C.map.playerStart);
  });

  it('matches the content behind its wording', () => {
    // "The first is there at the start".
    expect(C.waves[0]?.round).toBe(1);
    // "An enemy that arrived this round only moves": every wave has an arrival round.
    expect(C.rules.arrivalRound).toBe('all');
    // Corrode is "for the rest of the fight".
    expect(C.rules.statuses.corrode.duration).toBe(0);
    // "On a Guard corpse a spitter, on a Flamer corpse a burster"; "Medic and Warden corpses cannot hold one".
    expect(C.rules.eggs.corpseHosts).toEqual({ guard: 'spitter', flamer: 'burster', medic: null, warden: null });
    // Mine and burster blasts hit "there and on the 8 tiles around"; Spore Pod poisons "a 3x3 area".
    expect(C.abilities['proximity_mine']?.blast?.radius).toBe(1);
    expect(C.units['burster']?.blast?.radius).toBe(1);
    expect(C.abilities['spore_pod']?.area).toEqual({ shape: 'square', radius: 1 });
    // Pinned covers "its next phase" and Slowed "its next move".
    expect(C.rules.statuses.pinned.duration).toBe(1);
    expect(C.rules.statuses.slowed.duration).toBe(1);
    // The legend: walls and pillars block movement, sight and shots; the shutter and wall gun block movement and shots,
    // not sight; panels and vents block nothing. "Only walls and pillars block" sight, and they end the Flamer's sweep.
    const blocks = (t: string) => {
      const def = C.map.terrain[t];
      return def && { move: def.blocksMove, sight: def.blocksSight, shots: def.blocksProjectile };
    };
    for (const t of ['wall', 'pillar']) expect(blocks(t), t).toEqual({ move: true, sight: true, shots: true });
    for (const t of ['shutter', 'gun']) expect(blocks(t), t).toEqual({ move: true, sight: false, shots: true });
    for (const t of ['floor', 'panel', 'vent']) expect(blocks(t), t).toEqual({ move: false, sight: false, shots: false });
    expect(Object.keys(C.map.terrain).sort()).toEqual(['floor', 'gun', 'panel', 'pillar', 'shutter', 'vent', 'wall']);
    // "The elite" is the Warden, which the Grapple Hook cannot pull; the drone is the "flying minion".
    expect(Object.keys(C.units).filter((id) => C.units[id]!.elite)).toEqual(['warden']);
    expect(Object.keys(C.units).filter((id) => C.units[id]!.flying)).toEqual(['drone']);
    // "Electric, acid, poison and Parasite damage ignore it": every damage type content uses that armor does not reduce.
    const used = new Set<string>();
    const walk = (v: unknown): void => {
      if (Array.isArray(v)) v.forEach(walk);
      else if (v && typeof v === 'object') for (const [k, x] of Object.entries(v)) k === 'damageType' && typeof x === 'string' ? used.add(x) : walk(x);
    };
    walk(C.files);
    expect([...used].filter((t) => !(C.rules.armorAppliesTo as string[]).includes(t)).sort()).toEqual(['acid', 'electric', 'parasite', 'poison']);
    // Pick Up takes "an adjacent mine".
    expect(C.utilities['pick_up_mine']?.range).toBe(1);
    // The status table gives one duration each for poison and Parasite, whichever ability applies them.
    expect(C.abilities['spore_pod']?.status).toEqual({ id: 'poison', duration: C.rules.statuses.poison.duration });
    expect(C.abilities['parasite']?.status).toEqual({ id: 'parasite', duration: C.rules.statuses.parasite.duration });
  });
});
