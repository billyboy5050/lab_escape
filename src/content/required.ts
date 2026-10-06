// Fields the rules code reads without a fallback, by definition id, as dotted paths. A content edit that drops one is
// rejected on load, so a hot reload keeps the old values instead of crashing the first time the rule runs. Keep these
// in step with the non-null assertions in src/effects, src/ai and src/rules.

export const REQUIRED_ABILITY_FIELDS: Record<string, readonly string[]> = {
  sidearm: ['targeting.range'],
  grapple_hook: ['targeting.range'],
  proximity_mine: ['targeting.range', 'blast'],
  auto_turret: ['targeting.range', 'summon'],
  scout_drone: ['summon'],
  barrier_shield: ['targeting.range', 'segmentHp'],
  acid_spit: ['targeting.range'],
  lunge: ['targeting.range', 'damage', 'damageType'],
  spore_pod: ['targeting.range', 'area', 'status'],
  brood_egg: ['targeting.range'],
  parasite: ['targeting.range', 'status'],
};

/** Each unit's AI is chosen by which blocks it has, so a missing block would also change its role silently. */
export const REQUIRED_UNIT_FIELDS: Record<string, readonly string[]> = {
  guard: ['attack'],
  medic: ['heal', 'ai.safeDistance', 'ai.followDistance'],
  flamer: ['sweep', 'ai.scorePerPlayerSideHit', 'ai.scorePlayerBonus', 'ai.scorePerAllyHit'],
  warden: ['melee', 'charge'],
  turret: ['attack', 'ammo'],
  drone: ['zap', 'charges', 'returnPhases'],
  hatchling: ['melee'],
  spitter: ['attack'],
  burster: ['blast'],
};

export const REQUIRED_UPGRADE_FIELDS: Record<string, readonly string[]> = {
  A1_spore_burst: ['status', 'area.radius'],
  A3_parasite_jump: ['range'],
  H1_spore_mines: ['status'],
  H2_plague_drone: ['status'],
};

/** Utilities with a range read it without a fallback. */
export const REQUIRED_UTILITY_FIELDS: Record<string, readonly string[]> = {
  reload: ['range'],
  pick_up_mine: ['range'],
};

export const REQUIRED_UTILITIES = ['sprint', 'reload', 'pick_up_mine', 'redeploy'] as const;
