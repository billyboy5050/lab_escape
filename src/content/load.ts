import { DAMAGE_TYPES, type Content, type ContentFiles, type DamageType } from './types';
import { hashJson } from '../util/hash';
import { isTileName, parseTile } from '../util/tiles';

export class ContentError extends Error {
  constructor(public readonly problems: string[]) {
    super(`Content failed validation:\n  - ${problems.join('\n  - ')}`);
    this.name = 'ContentError';
  }
}

/** Validates the raw content files and builds the lookup tables the engine uses. Throws ContentError. */
export function buildContent(files: ContentFiles): Content {
  const problems = validateContent(files);
  if (problems.length) throw new ContentError(problems);
  const units = Object.fromEntries(files.units.map((u) => [u.id, u]));
  const abilities = Object.fromEntries(files.abilities.abilities.map((a) => [a.id, a]));
  const utilities = Object.fromEntries(files.abilities.utilities.map((u) => [u.id, u]));
  const upgrades = Object.fromEntries(files.upgrades.map((u) => [u.id, u]));
  const hazardDefs = Object.fromEntries(files.hazards.map((h) => [h.id, h]));
  return {
    files,
    rules: files.rules,
    units,
    abilities,
    abilityOrder: files.abilities.abilities.map((a) => a.id),
    utilities,
    upgrades,
    upgradeOrder: files.upgrades.map((u) => u.id),
    hazardDefs,
    map: files.map,
    waves: files.waves,
    intercom: files.intercom,
    presets: files.presets,
    hash: hashJson(files),
  };
}

/** Returns a list of human-readable problems; empty when the content is valid. */
export function validateContent(f: ContentFiles): string[] {
  const p: string[] = [];
  const int = (v: unknown, where: string, min = 0) => {
    if (typeof v !== 'number' || !Number.isInteger(v) || v < min) p.push(`${where} must be an integer >= ${min} (got ${JSON.stringify(v)})`);
  };
  const dtype = (v: unknown, where: string) => {
    if (!DAMAGE_TYPES.includes(v as DamageType)) p.push(`${where} has unknown damage type ${JSON.stringify(v)}`);
  };

  // Rules
  const r = f.rules;
  if (!r) return ['rules.json is missing'];
  int(r.turn?.ap, 'rules.turn.ap');
  int(r.turn?.movement, 'rules.turn.movement');
  int(r.sprint?.ap, 'rules.sprint.ap');
  int(r.sprint?.movement, 'rules.sprint.movement');
  for (const k of ['minions', 'eggs', 'mines', 'shieldPlacements'] as const) int(r.caps?.[k], `rules.caps.${k}`);
  int(r.statuses?.poison?.damage, 'rules.statuses.poison.damage');
  int(r.statuses?.poison?.duration, 'rules.statuses.poison.duration', 1);
  int(r.statuses?.poison?.spreadCap, 'rules.statuses.poison.spreadCap');
  dtype(r.statuses?.poison?.damageType, 'rules.statuses.poison');
  int(r.statuses?.parasite?.damage, 'rules.statuses.parasite.damage');
  int(r.statuses?.parasite?.duration, 'rules.statuses.parasite.duration', 1);
  dtype(r.statuses?.parasite?.damageType, 'rules.statuses.parasite');
  int(r.statuses?.corrode?.duration, 'rules.statuses.corrode.duration');
  int(r.statuses?.pinned?.duration, 'rules.statuses.pinned.duration', 1);
  int(r.statuses?.slowed?.duration, 'rules.statuses.slowed.duration', 1);
  int(r.statuses?.slowed?.movementPenalty, 'rules.statuses.slowed.movementPenalty');
  (r.armorAppliesTo ?? []).forEach((d, i) => dtype(d, `rules.armorAppliesTo[${i}]`));
  int(r.corpseDecay, 'rules.corpseDecay', 1);
  int(r.eggs?.hp, 'rules.eggs.hp', 1);
  int(r.eggs?.floorTimer, 'rules.eggs.floorTimer', 1);
  int(r.eggs?.corpseTimer, 'rules.eggs.corpseTimer', 1);
  int(r.eggs?.floorHatchlings, 'rules.eggs.floorHatchlings');
  int(r.chainDepthCap, 'rules.chainDepthCap', 1);
  int(r.roundCap, 'rules.roundCap', 1);
  if (!['all', 'first', 'none'].includes(r.arrivalRound)) p.push(`rules.arrivalRound must be all, first or none`);
  int(r.loadout?.abilities, 'rules.loadout.abilities', 1);
  int(r.loadout?.upgrades, 'rules.loadout.upgrades');

  // Units
  const unitIds = new Set<string>();
  for (const u of f.units ?? []) {
    const w = `units.${u.id}`;
    if (unitIds.has(u.id)) p.push(`${w} is defined twice`);
    unitIds.add(u.id);
    if (u.team !== 'player' && u.team !== 'enemy') p.push(`${w}.team must be player or enemy`);
    if (!['player', 'minion', 'enemy'].includes(u.kind)) p.push(`${w}.kind must be player, minion or enemy`);
    int(u.hp, `${w}.hp`, 1);
    int(u.armor, `${w}.armor`);
    int(u.move, `${w}.move`);
    for (const key of ['attack', 'melee', 'sweep', 'charge', 'zap', 'blast'] as const) {
      const block = u[key] as { damage?: number; damageType?: DamageType } | undefined;
      if (block) {
        int(block.damage, `${w}.${key}.damage`);
        dtype(block.damageType, `${w}.${key}`);
      }
    }
    if (u.attack) int(u.attack.range, `${w}.attack.range`, 1);
    if (u.zap) {
      int(u.zap.range, `${w}.zap.range`, 1);
      int(u.zap.chainDamage, `${w}.zap.chainDamage`);
    }
    if (u.charge) {
      int(u.charge.minDistance, `${w}.charge.minDistance`, 2);
      int(u.charge.maxMove, `${w}.charge.maxMove`, 1);
    }
  }
  for (const req of ['player', 'guard', 'medic', 'flamer', 'warden', 'turret', 'drone', 'hatchling', 'spitter', 'burster']) {
    if (!unitIds.has(req)) p.push(`units.json is missing "${req}"`);
  }
  if (!unitIds.has(r.eggs?.floorVariant)) p.push(`rules.eggs.floorVariant "${r.eggs?.floorVariant}" is not a unit`);
  if (!unitIds.has(r.parasiteHatchling)) p.push(`rules.parasiteHatchling "${r.parasiteHatchling}" is not a unit`);
  for (const [host, variant] of Object.entries(r.eggs?.corpseHosts ?? {})) {
    if (!unitIds.has(host)) p.push(`rules.eggs.corpseHosts key "${host}" is not a unit`);
    if (variant !== null && !unitIds.has(variant)) p.push(`rules.eggs.corpseHosts.${host} "${variant}" is not a unit`);
  }

  // Abilities and utilities
  const abilityIds = new Set<string>();
  for (const a of f.abilities?.abilities ?? []) {
    const w = `abilities.${a.id}`;
    abilityIds.add(a.id);
    int(a.ap, `${w}.ap`);
    if (a.damage !== undefined) int(a.damage, `${w}.damage`);
    if (a.damageType !== undefined) dtype(a.damageType, w);
    if (a.targeting?.range !== undefined) int(a.targeting.range, `${w}.targeting.range`, 1);
    if (a.summon && !unitIds.has(a.summon)) p.push(`${w}.summon "${a.summon}" is not a unit`);
    if (a.blast) {
      int(a.blast.damage, `${w}.blast.damage`);
      dtype(a.blast.damageType, `${w}.blast`);
    }
  }
  for (const req of ['sidearm', 'grapple_hook', 'proximity_mine', 'auto_turret', 'scout_drone', 'barrier_shield', 'acid_spit', 'lunge', 'spore_pod', 'brood_egg', 'parasite']) {
    if (!abilityIds.has(req)) p.push(`abilities.json is missing "${req}"`);
  }
  for (const u of f.abilities?.utilities ?? []) {
    int(u.ap, `utilities.${u.id}.ap`);
    if (u.requiresAbility && !abilityIds.has(u.requiresAbility)) p.push(`utilities.${u.id}.requiresAbility "${u.requiresAbility}" is not an ability`);
  }

  // Upgrades
  const upgradeIds = new Set<string>();
  for (const u of f.upgrades ?? []) {
    upgradeIds.add(u.id);
    for (const a of [...(u.requires?.all ?? []), ...(u.requires?.any ?? [])]) {
      if (!abilityIds.has(a)) p.push(`upgrades.${u.id} requires unknown ability "${a}"`);
    }
  }

  // Hazards and map
  const hazardIds = new Set((f.hazards ?? []).map((h) => h.id));
  for (const h of f.hazards ?? []) {
    int(h.damage, `hazards.${h.id}.damage`);
    dtype(h.damageType, `hazards.${h.id}`);
    int(h.cooldown, `hazards.${h.id}.cooldown`, 1);
    int(h.cancelCooldown, `hazards.${h.id}.cancelCooldown`, 1);
  }
  const m = f.map;
  if (!m) return [...p, 'map.json is missing'];
  if (m.rows.length !== m.height) p.push(`map has ${m.rows.length} rows but height ${m.height}`);
  m.rows.forEach((row, y) => {
    if (row.length !== m.width) p.push(`map row ${y + 1} has ${row.length} tiles but width ${m.width}`);
    for (const ch of row) {
      const t = m.legend[ch];
      if (!t) p.push(`map legend has no entry for "${ch}"`);
      else if (!m.terrain[t]) p.push(`map terrain has no definition for "${t}"`);
    }
  });
  const tileOk = (name: string, where: string) => {
    if (!isTileName(name)) return p.push(`${where} "${name}" is not a tile name`);
    const t = parseTile(name);
    if (t.x < 0 || t.y < 0 || t.x >= m.width || t.y >= m.height) p.push(`${where} "${name}" is off the map`);
  };
  tileOk(m.playerStart, 'map.playerStart');
  for (const hz of m.hazards ?? []) {
    if (!hazardIds.has(hz.def)) p.push(`map hazard ${hz.id} uses unknown hazard def "${hz.def}"`);
    hz.tiles.forEach((t) => tileOk(t, `map hazard ${hz.id} tile`));
    if (f.hazards.find((h) => h.id === hz.def)?.shape === 'lane' && !hz.direction) p.push(`map hazard ${hz.id} is a lane and needs a direction`);
  }

  // Waves
  let lastRound = 0;
  (f.waves ?? []).forEach((wave, i) => {
    int(wave.round, `waves[${i}].round`, 1);
    if (wave.round < lastRound) p.push(`waves[${i}] arrives before the wave listed above it`);
    lastRound = wave.round;
    for (const u of wave.units) {
      if (!unitIds.has(u.def)) p.push(`waves[${i}] uses unknown unit "${u.def}"`);
      tileOk(u.tile, `waves[${i}] spawn tile`);
    }
  });
  if (!f.waves?.length) p.push('waves.json has no waves');

  // Intercom
  const lineIds = new Set<string>();
  for (const l of f.intercom?.lines ?? []) {
    if (lineIds.has(l.id)) p.push(`intercom line "${l.id}" is defined twice`);
    lineIds.add(l.id);
    if (l.trigger.def && !unitIds.has(l.trigger.def)) p.push(`intercom line "${l.id}" names unknown unit "${l.trigger.def}"`);
  }

  // Presets
  for (const pr of f.presets ?? []) {
    for (const a of pr.abilities) if (!abilityIds.has(a)) p.push(`preset ${pr.id} uses unknown ability "${a}"`);
    for (const u of pr.upgrades) {
      if (!upgradeIds.has(u)) p.push(`preset ${pr.id} uses unknown upgrade "${u}"`);
      const def = f.upgrades.find((x) => x.id === u);
      if (def && !upgradeAllowed(def.requires, pr.abilities)) p.push(`preset ${pr.id} takes ${u} without its required abilities`);
    }
    if (pr.abilities.length > r.loadout.abilities) p.push(`preset ${pr.id} has more than ${r.loadout.abilities} abilities`);
    if (pr.upgrades.length > r.loadout.upgrades) p.push(`preset ${pr.id} has more than ${r.loadout.upgrades} upgrades`);
  }
  return p;
}

export function upgradeAllowed(requires: { all?: string[]; any?: string[] }, abilities: readonly string[]): boolean {
  const all = requires.all ?? [];
  const any = requires.any ?? [];
  return all.every((a) => abilities.includes(a)) && (any.length === 0 || any.some((a) => abilities.includes(a)));
}
