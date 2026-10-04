import { REQUIRED_ABILITY_FIELDS, REQUIRED_UNIT_FIELDS, REQUIRED_UPGRADE_FIELDS, REQUIRED_UTILITIES } from './required';
import { DAMAGE_TYPES, DIRECTION_NAMES, STATUS_IDS, type Content, type ContentFiles, type DamageType, type StatusId } from './types';
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
  /** Pushes a problem for each dotted path that is absent from an object, for fields the rules code needs. */
  const requireFields = (o: unknown, where: string, paths: readonly string[] = []) => {
    for (const path of paths) {
      const v = path.split('.').reduce<unknown>((n, k) => (n !== null && typeof n === 'object' ? (n as Record<string, unknown>)[k] : undefined), o);
      if (v === undefined || v === null) p.push(`${where}.${path} is missing`);
    }
  };
  /** The shape of an ability or upgrade `status` block, when there is one. */
  const status = (v: unknown, where: string) => {
    if (v === undefined) return;
    const s = (typeof v === 'object' && v !== null ? v : {}) as { id?: unknown; duration?: unknown };
    if (!STATUS_IDS.includes(s.id as StatusId)) p.push(`${where}.status has unknown status ${JSON.stringify(s.id)}`);
    int(s.duration, `${where}.status.duration`, 1);
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
    if (u.heal) {
      int(u.heal.amount, `${w}.heal.amount`, 1);
      int(u.heal.range, `${w}.heal.range`, 1);
    }
    if (u.sweep) int(u.sweep.length, `${w}.sweep.length`, 1);
    if (u.blast) int(u.blast.radius, `${w}.blast.radius`);
    if (u.purge) int(u.purge.cooldown, `${w}.purge.cooldown`, 1);
    if (u.ammo !== undefined) int(u.ammo, `${w}.ammo`);
    if (u.charges !== undefined) int(u.charges, `${w}.charges`, 1);
    requireFields(u, w, REQUIRED_UNIT_FIELDS[u.id]);
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
    requireFields(a, w, REQUIRED_ABILITY_FIELDS[a.id]);
    status(a.status, w);
    if (a.summon && !unitIds.has(a.summon)) p.push(`${w}.summon "${a.summon}" is not a unit`);
    if (a.segmentHp !== undefined) int(a.segmentHp, `${w}.segmentHp`, 1);
    if (a.area) int(a.area.radius, `${w}.area.radius`);
    if (a.blast) {
      int(a.blast.radius, `${w}.blast.radius`);
      int(a.blast.damage, `${w}.blast.damage`);
      dtype(a.blast.damageType, `${w}.blast`);
    }
  }
  for (const req of ['sidearm', 'grapple_hook', 'proximity_mine', 'auto_turret', 'scout_drone', 'barrier_shield', 'acid_spit', 'lunge', 'spore_pod', 'brood_egg', 'parasite']) {
    if (!abilityIds.has(req)) p.push(`abilities.json is missing "${req}"`);
  }
  const utilityIds = new Set((f.abilities?.utilities ?? []).map((u) => u.id));
  for (const req of REQUIRED_UTILITIES) {
    if (!utilityIds.has(req)) p.push(`abilities.json is missing utility "${req}"`);
  }
  // Sprint is defined twice: the engine charges rules.sprint, while the action list shows the utility entry.
  const sprint = (f.abilities?.utilities ?? []).find((u) => u.id === 'sprint');
  if (sprint && r.sprint) {
    if (sprint.ap !== r.sprint.ap) p.push(`utilities.sprint.ap (${sprint.ap}) must equal rules.sprint.ap (${r.sprint.ap})`);
    if (sprint.movement !== undefined && sprint.movement !== r.sprint.movement) p.push(`utilities.sprint.movement (${sprint.movement}) must equal rules.sprint.movement (${r.sprint.movement})`);
  }
  for (const u of f.abilities?.utilities ?? []) {
    int(u.ap, `utilities.${u.id}.ap`);
    if (u.requiresAbility && !abilityIds.has(u.requiresAbility)) p.push(`utilities.${u.id}.requiresAbility "${u.requiresAbility}" is not an ability`);
  }

  // Upgrades
  const upgradeIds = new Set<string>();
  for (const u of f.upgrades ?? []) {
    upgradeIds.add(u.id);
    requireFields(u, `upgrades.${u.id}`, REQUIRED_UPGRADE_FIELDS[u.id]);
    status(u.status, `upgrades.${u.id}`);
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
    // A direction that is not N, E, S or W would resolve to nothing, and the first lane shot would crash on it.
    if (hz.direction && !DIRECTION_NAMES.includes(hz.direction)) p.push(`map hazard ${hz.id} has unknown direction ${JSON.stringify(hz.direction)} (use N, E, S or W)`);
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
