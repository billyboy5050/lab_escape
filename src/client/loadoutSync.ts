import type { Content } from '../content/types';
import { loadoutProblems } from '../engine/step';
import type { Loadout } from '../state/types';

const sameList = (a: readonly string[], b: readonly string[]) => a.length === b.length && a.every((x, i) => x === b[i]);

export const sameLoadout = (a: Loadout, b: Loadout) => sameList(a.abilities, b.abilities) && sameList(a.upgrades, b.upgrades);

/**
 * Keeps a selected preset honest across a content reload. The loadout screen clears the preset id on any manual
 * edit, so a set id means the loadout is that preset's picks: take the edited preset's picks, or drop the label
 * when the preset is gone. Returns the same loadout object when nothing changes.
 */
export function syncPresetLoadout(c: Content, loadout: Loadout, presetId: string | null): { loadout: Loadout; presetId: string | null } {
  if (presetId === null) return { loadout, presetId };
  const preset = c.presets.find((p) => p.id === presetId);
  if (!preset) return { loadout, presetId: null };
  if (sameLoadout(loadout, preset)) return { loadout, presetId };
  return { loadout: { abilities: [...preset.abilities], upgrades: [...preset.upgrades] }, presetId };
}

/**
 * The loadout and preset label to use under the current content, and what is wrong with them, if anything.
 * The client calls this while rendering as well as when content reloads: a reload hands the new content to a
 * render before any effect can fix the loadout, and a fight cannot be built from a loadout that no longer fits.
 */
export function reconcileLoadout(c: Content, loadout: Loadout, presetId: string | null): { loadout: Loadout; presetId: string | null; problems: string[] } {
  const synced = syncPresetLoadout(c, loadout, presetId);
  return { ...synced, problems: loadoutProblems(c, synced.loadout) };
}

/** The preset whose picks are exactly this loadout, or null. */
export function presetIdFor(c: Content, loadout: Loadout): string | null {
  return c.presets.find((p) => sameLoadout(loadout, p))?.id ?? null;
}

/**
 * What "Retry this loadout" should start. The finished session is what the result screen shows, and a replay can
 * make it differ from the loadout on the loadout screen, so a retry takes the session's loadout. Returns the
 * current loadout object, and its label, when they already match.
 */
export function loadoutToRetry(c: Content, played: Loadout, current: Loadout, presetId: string | null): { loadout: Loadout; presetId: string | null } {
  if (sameLoadout(played, current)) return { loadout: current, presetId };
  return { loadout: { abilities: [...played.abilities], upgrades: [...played.upgrades] }, presetId: presetIdFor(c, played) };
}
